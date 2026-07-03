using NxCheck.Core.Checks.Support;
using NxCheck.Core.Expected;
using Xunit;

namespace NxCheck.Tests;

public class DropRateEvaluatorTests
{
    private static readonly DropSpec Spec = new() { WarnPct = 20, CriticalPct = 30, MinSamples = 100 };

    private static DropVerdict Eval(long r0, long d0, long r1, long d1) =>
        DropRateEvaluator.Evaluate(5140, new DropSample(r0, d0), new DropSample(r1, d1), Spec).Verdict;

    [Fact]
    public void Low_rate_is_ok() =>
        Assert.Equal(DropVerdict.Ok, Eval(1000, 0, 2000, 10)); // ~1%

    [Fact]
    public void Mid_rate_is_warning() =>
        Assert.Equal(DropVerdict.Warning, Eval(0, 0, 750, 250)); // 25%

    [Fact]
    public void High_rate_is_critical() =>
        Assert.Equal(DropVerdict.Critical, Eval(0, 0, 600, 400)); // 40%

    [Theory]
    [InlineData(800, 200, DropVerdict.Ok)]       // 정확히 20% → 경고 아님(> 비교)
    [InlineData(799, 201, DropVerdict.Warning)]  // 20.1%
    [InlineData(700, 300, DropVerdict.Warning)]  // 정확히 30% → 치명 아님
    [InlineData(699, 301, DropVerdict.Critical)] // 30.1%
    public void Threshold_boundaries(long dReceived, long dDrops, DropVerdict expected)
    {
        // 구간 델타가 (dReceived, dDrops)가 되도록 t0=0에서 시작
        var v = DropRateEvaluator.Evaluate(5140, new DropSample(0, 0), new DropSample(dReceived, dDrops), Spec).Verdict;
        Assert.Equal(expected, v);
    }

    [Fact]
    public void Below_min_samples_is_insufficient() =>
        Assert.Equal(DropVerdict.InsufficientTraffic, Eval(0, 0, 40, 10)); // 총 50 < 100

    [Fact]
    public void Negative_received_delta_is_reset() =>
        Assert.Equal(DropVerdict.CounterReset, Eval(5000, 100, 1000, 0)); // 재시작으로 카운터 감소

    [Fact]
    public void Negative_drops_delta_is_reset() =>
        Assert.Equal(DropVerdict.CounterReset, Eval(1000, 500, 5000, 100));

    [Fact]
    public void Rate_value_is_reported()
    {
        var a = DropRateEvaluator.Evaluate(5140, new DropSample(0, 0), new DropSample(700, 300), Spec);
        Assert.Equal(300, a.DeltaDrops);
        Assert.Equal(700, a.DeltaReceived);
        Assert.Equal(30.0, a.RatePct, precision: 1);
    }
}
