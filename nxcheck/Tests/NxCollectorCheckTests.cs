using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class NxCollectorCheckTests
{
    private const string EstabLine =
        "Recv-Q Send-Q Local Address:Port Peer Address:Port Process\n" +
        "0 0 10.0.0.5:40000 10.0.0.21:5141 users:((\"nxcollector\",pid=1,fd=7))";

    private static readonly string HeaderOnly = "Recv-Q Send-Q Local Address:Port Peer Address:Port Process";

    private static ExpectedSpec Spec(string host = "10.0.0.21", int port = 5141) =>
        new() { NxCollector = new NxCollectorSpec { Core = new CoreEndpoint { Host = host, Port = port } } };

    private static FakeCommandRunner Healthy(string ssOut, string restarts = "0") => new FakeCommandRunner()
        .WhenStdout("systemctl", ["is-active"], "active")
        .WhenStdout("systemctl", ["is-enabled"], "enabled")
        .WhenStdout("systemctl", ["show"], restarts)
        .WhenStdout("ss", ["dst"], ssOut);

    [Fact]
    public async Task Healthy_service_and_core_connected()
    {
        var ctx = TestContext.Build(Healthy(EstabLine), Spec());

        var results = await new NxCollectorCheck().RunAsync(ctx);

        Assert.Equal(CheckStatus.Pass, Single(results, "active").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "enabled").Status);
        var core = Single(results, "core 연결");
        Assert.Equal(CheckStatus.Pass, core.Status);
        Assert.Equal(Severity.Critical, core.Severity);
    }

    [Fact]
    public async Task Inactive_service_fails_critical()
    {
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active"], "inactive", exitCode: 3)
            .WhenStdout("systemctl", ["is-enabled"], "enabled")
            .WhenStdout("ss", ["dst"], EstabLine);
        var ctx = TestContext.Build(runner, Spec());

        var results = await new NxCollectorCheck().RunAsync(ctx);

        var active = Single(results, "active");
        Assert.Equal(CheckStatus.Fail, active.Status);
        Assert.Equal(Severity.Critical, active.Severity);
    }

    [Fact]
    public async Task Core_skipped_when_expected_missing()
    {
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("systemctl", ["is-enabled"], "enabled");
        var ctx = TestContext.Build(runner); // NxCollector 스펙 없음

        var results = await new NxCollectorCheck().RunAsync(ctx);

        Assert.Equal(CheckStatus.Skip, Single(results, "core 연결").Status);
    }

    [Fact]
    public async Task Core_fails_when_no_established_socket()
    {
        var ctx = TestContext.Build(Healthy(HeaderOnly), Spec());

        var results = await new NxCollectorCheck().RunAsync(ctx);

        var core = Single(results, "core 연결");
        Assert.Equal(CheckStatus.Fail, core.Status);
        Assert.Contains(core.Ladder, s => s.Label.Contains("ESTAB") && s.Status == CheckStatus.Fail);
        Assert.Contains(core.Ladder, s => s.Label.Contains("해석") && s.Status == CheckStatus.Pass);
    }

    [Fact]
    public async Task Core_resolves_hostname_via_getent()
    {
        var runner = Healthy(EstabLine)
            .WhenStdout("getent", ["hosts"], "10.0.0.21   01.nxportrait.core");
        var ctx = TestContext.Build(runner, Spec(host: "01.nxportrait.core"));

        var results = await new NxCollectorCheck().RunAsync(ctx);

        var core = Single(results, "core 연결");
        Assert.Equal(CheckStatus.Pass, core.Status);
        // PASS는 사다리를 접으므로, getent로 해석된 IP는 Actual로 확인.
        Assert.Contains("10.0.0.21", core.Actual);
        Assert.Contains(runner.Calls, c => c.File == "getent"); // 해석 경로를 실제로 탔는지

    }

    [Fact]
    public async Task Unresolvable_host_fails_before_socket_check()
    {
        // getent 미설정 → 해석 실패
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("systemctl", ["is-enabled"], "enabled");
        var ctx = TestContext.Build(runner, Spec(host: "no.such.host"));

        var results = await new NxCollectorCheck().RunAsync(ctx);

        var core = Single(results, "core 연결");
        Assert.Equal(CheckStatus.Fail, core.Status);
        Assert.Contains(core.Ladder, s => s.Label.Contains("해석") && s.Status == CheckStatus.Fail);
        // 해석 실패면 소켓 단계는 시도조차 안 함
        Assert.DoesNotContain(core.Ladder, s => s.Label.Contains("ESTAB"));
    }

    [Fact]
    public async Task Crashloop_only_in_flow_depth()
    {
        var staticCtx = TestContext.Build(Healthy(EstabLine), Spec(), depth: CheckDepth.Static);
        var staticResults = await new NxCollectorCheck().RunAsync(staticCtx);
        Assert.DoesNotContain(staticResults, r => r.Item == "크래시루프");

        var flowCtx = TestContext.Build(Healthy(EstabLine), Spec(), depth: CheckDepth.Flow);
        var flowResults = await new NxCollectorCheck().RunAsync(flowCtx);
        Assert.Equal(CheckStatus.Pass, Single(flowResults, "크래시루프").Status);
    }

    [Fact]
    public async Task Crashloop_fails_when_restarts_positive()
    {
        var runner = Healthy(EstabLine, restarts: "3"); // NRestarts=3
        var ctx = TestContext.Build(runner, Spec(), depth: CheckDepth.Flow);

        var results = await new NxCollectorCheck().RunAsync(ctx);

        var cl = Single(results, "크래시루프");
        Assert.Equal(CheckStatus.Fail, cl.Status);
        Assert.Equal("3", cl.Actual);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
