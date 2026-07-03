using NxCheck.Core.Expected;

namespace NxCheck.Core.Checks.Support;

/// <summary>한 시점의 포트별 카운터(누적).</summary>
public readonly record struct DropSample(long Received, long Drops);

public enum DropVerdict
{
    Ok,
    Warning,
    Critical,
    /// <summary>구간 표본이 최소치 미만 — 판정 보류(저트래픽 false FAIL 방지).</summary>
    InsufficientTraffic,
    /// <summary>음수 delta — 카운터 리셋(재시작/재부팅) 감지, 이번 구간 skip.</summary>
    CounterReset,
}

/// <param name="RatePct">Δ드랍 / Δ(수신+드랍) × 100.</param>
public readonly record struct DropAssessment(
    int Port, DropVerdict Verdict, long DeltaReceived, long DeltaDrops, double RatePct);

/// <summary>
/// 드랍률 구간(delta) 평가 — 설계 4.8 flow 확정 사항.
/// 두 시점 카운터로 Δ드랍/Δ(수신+드랍)을 구하고 2단 임계(warn/crit)로 판정.
/// - 음수 Δ → 카운터 리셋 → skip
/// - Δ(수신+드랍) &lt; 최소표본 → 판정 보류(저트래픽)
/// 순수 함수라 시간·IO 없이 테스트 가능(로직의 핵심).
/// </summary>
public static class DropRateEvaluator
{
    public static DropAssessment Evaluate(int port, DropSample t0, DropSample t1, DropSpec spec)
    {
        var dReceived = t1.Received - t0.Received;
        var dDrops = t1.Drops - t0.Drops;

        if (dReceived < 0 || dDrops < 0)
            return new DropAssessment(port, DropVerdict.CounterReset, dReceived, dDrops, 0);

        var total = dReceived + dDrops;
        if (total < spec.MinSamples)
            return new DropAssessment(port, DropVerdict.InsufficientTraffic, dReceived, dDrops, 0);

        var rate = (double)dDrops / total * 100.0;

        var verdict =
            rate > spec.CriticalPct ? DropVerdict.Critical :
            rate > spec.WarnPct ? DropVerdict.Warning :
            DropVerdict.Ok;

        return new DropAssessment(port, verdict, dReceived, dDrops, rate);
    }
}
