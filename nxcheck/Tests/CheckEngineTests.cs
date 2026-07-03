using NxCheck.Core.Checks;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class CheckEngineTests
{
    private sealed class StubCheck(string module, params CheckResult[] results) : ICheck
    {
        public string Module => module;
        public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CheckResult>>(results);
    }

    private sealed class ThrowingCheck : ICheck
    {
        public string Module => "boom";
        public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("터짐");
    }

    private static CheckContext Ctx() => TestContext.Build(new FakeCommandRunner());

    [Fact]
    public async Task Runs_all_checks_and_flattens()
    {
        var engine = new CheckEngine([
            new StubCheck("a", CheckResult.Pass("a", "x", Severity.Info)),
            new StubCheck("b",
                CheckResult.Pass("b", "y", Severity.Info),
                CheckResult.Fail("b", "z", Severity.Warning)),
        ]);

        var results = await engine.RunAllAsync(Ctx());

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Throwing_check_becomes_error_and_others_still_run()
    {
        var engine = new CheckEngine([
            new ThrowingCheck(),
            new StubCheck("a", CheckResult.Pass("a", "x", Severity.Info)),
        ]);

        var results = await engine.RunAllAsync(Ctx());

        var err = Assert.Single(results, r => r.Module == "boom");
        Assert.Equal(CheckStatus.Error, err.Status);
        Assert.Equal(Severity.Critical, err.Severity);
        Assert.Contains(results, r => r.Module == "a" && r.Status == CheckStatus.Pass);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var engine = new CheckEngine([new StubCheck("a", CheckResult.Pass("a", "x", Severity.Info))]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => engine.RunAllAsync(Ctx(), cts.Token));
    }
}
