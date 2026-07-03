using NxCheck.Core.Checks.Support;
using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.4 syslog-ng (다중 인스턴스 — 포트당 1개, 5140~5144 가변).
/// - 인스턴스별 active + enabled (UnitTemplate 있으면 포트별 루프, 없으면 단일 "syslog-ng")
/// - `syslog-ng -s` 문법
/// - UDP 포트 바인드(ss -ulnp, 프로세스 syslog-ng)
/// - rsyslog 비활성(충돌 방지)
/// 포트 정합(nginx↔)은 4.8 crosscheck.
/// </summary>
public sealed class SyslogNgCheck : ICheck
{
    public string Module => "syslog-ng";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var spec = ctx.Expected.Syslog;
        var probe = new SystemdProbe(ctx.Runner);
        var results = new List<CheckResult>();

        // 서비스 인스턴스
        if (spec is { UnitTemplate: { } tmpl, Ports: { Count: > 0 } ports })
        {
            foreach (var port in ports)
            {
                var unit = tmpl.Replace("{port}", port.ToString());
                results.Add(await ServiceResultAsync(probe, unit, $"인스턴스 {port} active", ct));
            }
        }
        else
        {
            results.Add(await ServiceResultAsync(probe, "syslog-ng", "active", ct));
        }

        // 문법
        var syntax = await ctx.Runner.RunAsync("syslog-ng", ["-s"], ct: ct);
        results.Add(!syntax.Started
            ? CheckResult.Error(Module, "문법(-s)", Severity.Critical, hint: "syslog-ng 실행 실패")
            : syntax.Success
                ? CheckResult.Pass(Module, "문법(-s)", Severity.Critical, expected: "통과", actual: "통과")
                : CheckResult.Fail(Module, "문법(-s)", Severity.Critical, expected: "통과",
                    actual: $"exit {syntax.ExitCode}", hint: syntax.StdErr.Trim()));

        // UDP 바인드
        if (spec?.Ports is { Count: > 0 } expectedPorts)
            results.Add(await CheckBindAsync(ctx, expectedPorts, ct));

        // rsyslog 비활성
        results.Add(await CheckRsyslogInactiveAsync(probe, ct));

        return results;
    }

    private async Task<CheckResult> ServiceResultAsync(SystemdProbe probe, string unit, string item, CancellationToken ct)
    {
        var st = await probe.IsActiveAsync(unit, ct);
        return !st.Queried
            ? CheckResult.Error(Module, item, Severity.Critical, hint: "systemctl 실행 실패")
            : st.Ok
                ? CheckResult.Pass(Module, item, Severity.Critical, expected: "active", actual: st.Raw)
                : CheckResult.Fail(Module, item, Severity.Critical, expected: "active", actual: st.Raw,
                    hint: $"{unit} 미실행");
    }

    private async Task<CheckResult> CheckBindAsync(CheckContext ctx, List<int> expectedPorts, CancellationToken ct)
    {
        var ss = await ctx.Runner.RunAsync("ss", ["-ulnp"], ct: ct);
        if (!ss.Started)
            return CheckResult.Error(Module, "UDP 바인드", Severity.Critical, hint: $"ss 실행 실패: {ss.FailureReason}");

        var bound = SsParser.UdpListenerPorts(ss.StdOut, processContains: "syslog-ng");
        var missing = expectedPorts.Where(p => !bound.Contains(p)).OrderBy(p => p).ToList();

        return missing.Count == 0
            ? CheckResult.Pass(Module, "UDP 바인드", Severity.Critical,
                expected: $"{string.Join(",", expectedPorts)} 바인드", actual: "모두 바인드됨")
            : CheckResult.Fail(Module, "UDP 바인드", Severity.Critical,
                expected: $"{string.Join(",", expectedPorts)} 바인드", actual: $"미바인드: {string.Join(",", missing)}",
                hint: "syslog-ng가 해당 UDP 포트를 열지 못함 — source 누락 또는 바인드 실패");
    }

    private async Task<CheckResult> CheckRsyslogInactiveAsync(SystemdProbe probe, CancellationToken ct)
    {
        var st = await probe.IsActiveAsync("rsyslog", ct);
        if (!st.Queried)
            return CheckResult.Skip(Module, "rsyslog 비활성", hint: "systemctl 실행 실패");

        return st.Ok
            ? CheckResult.Fail(Module, "rsyslog 비활성", Severity.Warning, expected: "inactive", actual: "active",
                hint: "rsyslog가 활성 — syslog-ng와 UDP 포트 충돌 위험")
            : CheckResult.Pass(Module, "rsyslog 비활성", Severity.Warning, expected: "inactive", actual: st.Raw);
    }
}
