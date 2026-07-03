using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class ExitCodePolicyTests
{
    private static CheckResult R(CheckStatus status, Severity severity) =>
        new() { Module = "m", Item = "i", Status = status, Severity = severity };

    [Theory]
    [InlineData(CheckStatus.Pass, Severity.Critical, 0)]
    [InlineData(CheckStatus.Skip, Severity.Critical, 0)]
    [InlineData(CheckStatus.Fail, Severity.Info, 0)]
    [InlineData(CheckStatus.Fail, Severity.Warning, 1)]
    [InlineData(CheckStatus.Fail, Severity.Critical, 2)]
    [InlineData(CheckStatus.Error, Severity.Warning, 1)]
    [InlineData(CheckStatus.Error, Severity.Critical, 2)] // 못 돌아도 critical이면 2
    [InlineData(CheckStatus.Error, Severity.Info, 0)]
    public void ForResult_maps_status_and_severity(CheckStatus status, Severity severity, int expected) =>
        Assert.Equal(expected, ExitCodePolicy.ForResult(R(status, severity)));

    [Fact]
    public void Aggregate_takes_max()
    {
        var results = new[]
        {
            R(CheckStatus.Pass, Severity.Critical),
            R(CheckStatus.Fail, Severity.Warning),   // 1
            R(CheckStatus.Fail, Severity.Critical),  // 2
        };
        Assert.Equal(2, ExitCodePolicy.Aggregate(results));
    }

    [Fact]
    public void Aggregate_empty_is_zero() =>
        Assert.Equal(0, ExitCodePolicy.Aggregate([]));

    [Fact]
    public void Aggregate_all_pass_is_zero()
    {
        var results = new[] { R(CheckStatus.Pass, Severity.Critical), R(CheckStatus.Skip, Severity.Warning) };
        Assert.Equal(0, ExitCodePolicy.Aggregate(results));
    }
}
