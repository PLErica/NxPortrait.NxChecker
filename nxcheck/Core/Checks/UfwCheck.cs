using NxCheck.Core.Checks.Support;
using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.7 ufw. 읽기 소스: `ufw status verbose` + `/etc/ufw/before.rules` 직독(iptables 백엔드 불필요).
/// - active + 부팅 시 enable
/// - default incoming=deny · outgoing=allow
/// - syslog UDP 5140~5144 ⊆ 허용 포트 (막혀서 패킷 안 닿는 경우 탐지)
/// - drift: 기대 외 열린 포트 없음
/// - before.rules ICMP echo-request(ping) drop 설정
/// </summary>
public sealed class UfwCheck(string beforeRulesPath = "/etc/ufw/before.rules") : ICheck
{
    public string Module => "ufw";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        // 부팅 시 자동 시작 (systemd)
        var enabled = await new SystemdProbe(ctx.Runner).IsEnabledAsync("ufw", ct);
        results.Add(!enabled.Queried
            ? CheckResult.Error(Module, "enabled(부팅)", Severity.Critical, hint: "systemctl 실행 실패")
            : enabled.Ok
                ? CheckResult.Pass(Module, "enabled(부팅)", Severity.Critical, expected: "enabled", actual: enabled.Raw)
                : CheckResult.Fail(Module, "enabled(부팅)", Severity.Critical, expected: "enabled", actual: enabled.Raw,
                    hint: "재부팅 시 방화벽이 안 올라옴"));

        // ufw status verbose
        var status = await ctx.Runner.RunAsync("ufw", ["status", "verbose"], ct: ct);
        if (!status.Started)
        {
            results.Add(CheckResult.Error(Module, "status", Severity.Critical,
                hint: $"ufw 실행 실패: {status.FailureReason}"));
        }
        else
        {
            var s = UfwStatusParser.Parse(status.StdOut);
            AddStatusResults(results, ctx, s);
        }

        // before.rules ICMP drop
        results.Add(CheckIcmpDrop(ctx));

        return results;
    }

    private void AddStatusResults(List<CheckResult> results, CheckContext ctx, UfwStatus s)
    {
        results.Add(s.Active
            ? CheckResult.Pass(Module, "active", Severity.Critical, expected: "active", actual: "active")
            : CheckResult.Fail(Module, "active", Severity.Critical, expected: "active", actual: "inactive",
                hint: "ufw가 비활성 — 방화벽 미적용"));

        // default 정책
        var incOk = s.DefaultIncoming == "deny";
        var outOk = s.DefaultOutgoing == "allow";
        results.Add(incOk && outOk
            ? CheckResult.Pass(Module, "default 정책", Severity.Critical,
                expected: "deny(in)/allow(out)", actual: $"{s.DefaultIncoming}(in)/{s.DefaultOutgoing}(out)")
            : CheckResult.Fail(Module, "default 정책", Severity.Critical,
                expected: "deny(in)/allow(out)", actual: $"{s.DefaultIncoming}(in)/{s.DefaultOutgoing}(out)",
                hint: "기본 정책이 기대와 다름"));

        // syslog 포트 ⊆ 허용 (교차검증)
        if (ctx.Expected.Syslog?.Ports is { Count: > 0 } syslogPorts)
        {
            var blocked = syslogPorts.Where(p => !s.AllowedPorts.Contains(p)).ToList();
            results.Add(blocked.Count == 0
                ? CheckResult.Pass(Module, "syslog 포트 허용", Severity.Critical,
                    expected: $"{Join(syslogPorts)} 허용", actual: "모두 허용")
                : CheckResult.Fail(Module, "syslog 포트 허용", Severity.Critical,
                    expected: $"{Join(syslogPorts)} 허용", actual: $"차단됨: {Join(blocked)}",
                    hint: "ufw가 막아 syslog UDP 패킷이 리스너에 안 닿음"));
        }

        // drift: 기대 외 열린 포트
        if (ctx.Expected.Ufw?.AllowedPorts is { Count: > 0 } expectedPorts)
        {
            var expectedSet = expectedPorts.ToHashSet();
            var drift = s.AllowedPorts.Where(p => !expectedSet.Contains(p)).OrderBy(p => p).ToList();
            results.Add(drift.Count == 0
                ? CheckResult.Pass(Module, "포트 drift", Severity.Warning,
                    expected: "기대 외 열린 포트 없음", actual: "없음")
                : CheckResult.Fail(Module, "포트 drift", Severity.Warning,
                    expected: "기대 외 열린 포트 없음", actual: $"추가로 열림: {Join(drift)}",
                    hint: "기대 스펙에 없는 포트가 열려 있음"));
        }
    }

    private CheckResult CheckIcmpDrop(CheckContext ctx)
    {
        if (ctx.Expected.Ufw is { RequireIcmpDrop: false })
            return CheckResult.Skip(Module, "ICMP ping drop", hint: "요구 안 함(require_icmp_drop: false)");

        if (!File.Exists(beforeRulesPath))
            return CheckResult.Error(Module, "ICMP ping drop", Severity.Warning,
                hint: $"{beforeRulesPath} 없음");

        var echoLines = File.ReadLines(beforeRulesPath)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#'))
            .Where(l => l.Contains("icmp", StringComparison.OrdinalIgnoreCase)
                     && l.Contains("echo-request", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dropped = echoLines.Any(l => l.Contains("-j DROP", StringComparison.OrdinalIgnoreCase)
                                       || l.Contains("-j REJECT", StringComparison.OrdinalIgnoreCase));
        var accepted = echoLines.Any(l => l.Contains("-j ACCEPT", StringComparison.OrdinalIgnoreCase));

        // DROP/REJECT 룰이 있거나, echo-request ACCEPT 룰이 아예 없으면(기본 정책에 맡김) 통과로 본다.
        return dropped || !accepted
            ? CheckResult.Pass(Module, "ICMP ping drop", Severity.Warning,
                expected: "ping drop", actual: dropped ? "DROP 룰 존재" : "ACCEPT 룰 없음")
            : CheckResult.Fail(Module, "ICMP ping drop", Severity.Warning,
                expected: "ping drop", actual: "echo-request ACCEPT",
                hint: "before.rules에서 ICMP echo-request가 허용됨 — ping drop 미설정");
    }

    private static string Join(IEnumerable<int> ports) => string.Join(",", ports);
}
