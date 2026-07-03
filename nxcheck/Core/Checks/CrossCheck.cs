using NxCheck.Core.Checks.Support;
using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.8 nginx ↔ syslog-ng 포트 정합(contract 체크).
/// 두 config가 서로 약속을 지키는지 — 갯수가 아니라 <b>집합</b>을 비교(off-by-one 탐지).
/// - nginx: `nginx -T` 덤프의 stream upstream server 포트
/// - syslog-ng: `ss -ulnp`로 실제 바인드된 UDP 리스너 포트(프로세스 syslog-ng)
/// 선행 의존: `nginx -T` 실패 시 비교 불가 → ERROR.
/// (flow의 포트별 드랍률은 별도 — 설계 4.8 flow / 드랍 delta 모듈)
/// </summary>
public sealed class CrossCheck : ICheck
{
    private const string Item = "nginx↔syslog-ng 포트 정합";

    public string Module => "crosscheck";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        // 선행: nginx -T
        var nginxT = await ctx.Runner.RunAsync("nginx", ["-T"], ct: ct);
        if (!nginxT.Started || !nginxT.Success)
            return [CheckResult.Error(Module, Item, Severity.Critical,
                hint: $"nginx -T 실패로 비교 불가(선행 의존): {nginxT.FailureReason}")];

        var nginxPorts = NginxStreamParser.UpstreamPorts(nginxT.StdOut);
        if (nginxPorts.Count == 0)
            return [CheckResult.Skip(Module, Item, hint: "nginx stream upstream 포트를 찾지 못함(stream 미구성?)")];

        // syslog-ng 실제 리스너
        var ss = await ctx.Runner.RunAsync("ss", ["-ulnp"], ct: ct);
        if (!ss.Started)
            return [CheckResult.Error(Module, Item, Severity.Critical, hint: $"ss 실패: {ss.FailureReason}")];

        var syslogPorts = SsParser.UdpListenerPorts(ss.StdOut, processContains: "syslog-ng");

        var missingInSyslog = nginxPorts.Except(syslogPorts).OrderBy(p => p).ToList(); // nginx 분배, syslog 안 들음 → 유실
        var orphanInSyslog = syslogPorts.Except(nginxPorts).OrderBy(p => p).ToList();  // syslog 듣는데 송신 없음

        if (missingInSyslog.Count == 0 && orphanInSyslog.Count == 0)
            return [CheckResult.Pass(Module, Item, Severity.Critical,
                expected: Join(nginxPorts), actual: "집합 일치")];

        var ladder = new List<LadderStep>();
        foreach (var p in missingInSyslog)
            ladder.Add(new LadderStep($"{p}", CheckStatus.Fail, "nginx는 분배하는데 syslog-ng가 안 듣는 중 (로그 유실)"));
        foreach (var p in orphanInSyslog)
            ladder.Add(new LadderStep($"{p}", CheckStatus.Fail, "syslog-ng는 듣는데 nginx가 안 보냄 (송신자 없음)"));

        return [CheckResult.Fail(Module, Item, Severity.Critical,
            expected: $"nginx={Join(nginxPorts)} == syslog={Join(syslogPorts)}",
            actual: $"유실={Join(missingInSyslog)} 고아={Join(orphanInSyslog)}",
            ladder: ladder,
            hint: "두 config의 포트 집합 불일치 — syslog-ng source 누락/바인드 실패 또는 nginx 타깃 오설정")];
    }

    private static string Join(IEnumerable<int> ports) => string.Join(",", ports.OrderBy(p => p));
}
