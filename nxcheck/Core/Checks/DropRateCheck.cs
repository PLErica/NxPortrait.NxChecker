using NxCheck.Core.Checks.Support;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.8 flow — 포트별 드랍률(구간 delta). flow 깊이에서만 동작.
/// 두 시점(ctx.SampleWindow 간격)의 syslog-ng-ctl stats로 Δ드랍/Δ(수신+드랍)을 구해
/// 2단 임계(warn/crit)로 판정하고, 커널 소켓 드랍(/proc/net/udp)을 교차신호로 사다리에 붙인다.
/// 저트래픽·카운터 리셋은 판정 보류(skip).
/// </summary>
public sealed class DropRateCheck(string procUdpPath = "/proc/net/udp") : ICheck
{
    public string Module => "drop";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        if (!ctx.IncludesFlow)
            return [CheckResult.Skip(Module, "드랍률", hint: "flow 깊이에서만 측정(출고 후)")];

        if (ctx.Expected.Syslog?.Ports is not { Count: > 0 } ports)
            return [CheckResult.Skip(Module, "드랍률", hint: "syslog 포트 미정의")];

        var spec = ctx.Expected.Drop ?? new DropSpec();

        var b0 = await ReadStatsAsync(ctx, ct);
        if (b0 is null)
            return [CheckResult.Error(Module, "드랍률", Severity.Warning, hint: "syslog-ng-ctl stats 실행 실패")];
        var a0 = ReadKernelDrops();

        await Task.Delay(ctx.SampleWindow, ct);

        var b1 = await ReadStatsAsync(ctx, ct);
        if (b1 is null)
            return [CheckResult.Error(Module, "드랍률", Severity.Warning, hint: "syslog-ng-ctl stats 실행 실패(2차)")];
        var a1 = ReadKernelDrops();

        var results = new List<CheckResult>();
        foreach (var port in ports)
        {
            if (!b0.TryGetValue(port, out var s0) || !b1.TryGetValue(port, out var s1))
            {
                results.Add(CheckResult.Skip(Module, $"{port} 드랍률", hint: "syslog-ng stats에 해당 포트 없음"));
                continue;
            }

            var assess = DropRateEvaluator.Evaluate(port, s0, s1, spec);
            var kernelDelta = KernelDelta(a0, a1, port);
            results.Add(Map(assess, spec, kernelDelta));
        }

        return results;
    }

    private CheckResult Map(DropAssessment a, DropSpec spec, long? kernelDelta)
    {
        var item = $"{a.Port} 드랍률";
        var stat = $"수신 {a.DeltaReceived:N0} 드랍 {a.DeltaDrops:N0} → {a.RatePct:0.0}%";

        return a.Verdict switch
        {
            DropVerdict.Ok => CheckResult.Pass(Module, item, Severity.Warning,
                expected: $"≤{spec.WarnPct}%", actual: stat),

            DropVerdict.InsufficientTraffic => CheckResult.Skip(Module, item,
                hint: $"트래픽 부족(표본 {a.DeltaReceived + a.DeltaDrops} < {spec.MinSamples}) — 판정 보류"),

            DropVerdict.CounterReset => CheckResult.Skip(Module, item,
                hint: "음수 Δ — 카운터 리셋(재시작/재부팅) 감지, 이번 구간 보류"),

            DropVerdict.Warning => CheckResult.Fail(Module, item, Severity.Warning,
                expected: $"≤{spec.WarnPct}%", actual: stat, ladder: KernelLadder(kernelDelta),
                hint: "드랍률 경고 임계 초과"),

            DropVerdict.Critical => CheckResult.Fail(Module, item, Severity.Critical,
                expected: $"≤{spec.CriticalPct}%", actual: stat, ladder: KernelLadder(kernelDelta),
                hint: "드랍률 치명 임계 초과 — net.core.rmem 또는 source flags(so-rcvbuf) 확인"),

            _ => CheckResult.Skip(Module, item),
        };
    }

    private static IReadOnlyList<LadderStep> KernelLadder(long? kernelDelta) =>
        kernelDelta is { } d
            ? [new LadderStep("커널 소켓 드랍 Δ", d > 0 ? CheckStatus.Fail : CheckStatus.Pass,
                d > 0 ? $"+{d} (수신버퍼 넘침 의심)" : "+0 (커널단 정상 → 앱 소비 지연 의심)")]
            : [];

    private static long? KernelDelta(IReadOnlyDictionary<int, long> a0, IReadOnlyDictionary<int, long> a1, int port)
    {
        if (!a0.TryGetValue(port, out var d0) || !a1.TryGetValue(port, out var d1))
            return null;
        return d1 - d0;
    }

    private static async Task<IReadOnlyDictionary<int, DropSample>?> ReadStatsAsync(CheckContext ctx, CancellationToken ct)
    {
        var r = await ctx.Runner.RunAsync("syslog-ng-ctl", ["stats"], ct: ct);
        return r.Started ? SyslogNgStatsParser.Parse(r.StdOut) : null;
    }

    private IReadOnlyDictionary<int, long> ReadKernelDrops() =>
        File.Exists(procUdpPath)
            ? ProcNetUdpParser.SocketDrops(File.ReadAllText(procUdpPath))
            : new Dictionary<int, long>();
}
