using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class SyslogNgCheckTests
{
    private static string Ss(params int[] ports) =>
        "State Recv-Q Send-Q Local Address:Port Peer Address:Port Process\n" + string.Join("\n",
            ports.Select(p => $"UNCONN 0 0 0.0.0.0:{p} 0.0.0.0:* users:((\"syslog-ng\",pid=1,fd=5))"));

    // rsyslog inactive + syntax ok + ss 바인드. is-active는 generic으로 active.
    private static FakeCommandRunner BaseRunner(string ss) => new FakeCommandRunner()
        .WhenStdout("systemctl", ["is-active", "rsyslog"], "inactive", exitCode: 3)
        .WhenStdout("systemctl", ["is-active"], "active")
        .WhenStdout("systemctl", ["is-enabled"], "enabled")
        .WhenStdout("syslog-ng", ["-s"], "")
        .WhenStdout("ss", ["-ulnp"], ss);

    private static ExpectedSpec Spec(int[] ports, string? template = null) =>
        new() { Syslog = new SyslogSpec { Ports = [.. ports], UnitTemplate = template } };

    [Fact]
    public async Task Single_unit_healthy_passes()
    {
        var ports = new[] { 5140, 5141, 5142, 5143, 5144 };
        var results = await new SyslogNgCheck().RunAsync(
            TestContext.Build(BaseRunner(Ss(ports)), Spec(ports)));

        Assert.Equal(CheckStatus.Pass, Single(results, "active").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "문법(-s)").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "UDP 바인드").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "rsyslog 비활성").Status);
    }

    [Fact]
    public async Task Multi_instance_one_down_fails_that_instance()
    {
        var ports = new[] { 5140, 5141, 5142 };
        // 구체적 매처(5142 down)를 generic보다 먼저 등록해야 순서상 우선 매칭됨
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active", "syslog-ng@5142"], "failed", exitCode: 3)
            .WhenStdout("systemctl", ["is-active", "rsyslog"], "inactive", exitCode: 3)
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("syslog-ng", ["-s"], "")
            .WhenStdout("ss", ["-ulnp"], Ss(ports));

        var results = await new SyslogNgCheck().RunAsync(
            TestContext.Build(runner, Spec(ports, template: "syslog-ng@{port}")));

        Assert.Equal(CheckStatus.Fail, Single(results, "인스턴스 5142 active").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "인스턴스 5140 active").Status);
    }

    [Fact]
    public async Task Syntax_error_fails()
    {
        var ports = new[] { 5140 };
        var runner = new FakeCommandRunner()
            .When("syslog-ng", ["-s"], new NxCheck.Core.Runners.CommandResult
            {
                FileName = "syslog-ng", Arguments = "", Started = true, ExitCode = 1, StdErr = "parse error",
            })
            .WhenStdout("systemctl", ["is-active", "rsyslog"], "inactive", exitCode: 3)
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("ss", ["-ulnp"], Ss(ports));

        var results = await new SyslogNgCheck().RunAsync(TestContext.Build(runner, Spec(ports)));

        Assert.Equal(CheckStatus.Fail, Single(results, "문법(-s)").Status);
    }

    [Fact]
    public async Task Missing_bind_fails()
    {
        // 5144는 바인드 안 됨
        var runner = BaseRunner(Ss(5140, 5141, 5142, 5143));
        var results = await new SyslogNgCheck().RunAsync(
            TestContext.Build(runner, Spec([5140, 5141, 5142, 5143, 5144])));

        var r = Single(results, "UDP 바인드");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("5144", r.Actual);
    }

    [Fact]
    public async Task Rsyslog_active_fails()
    {
        var ports = new[] { 5140 };
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active", "rsyslog"], "active")
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("syslog-ng", ["-s"], "")
            .WhenStdout("ss", ["-ulnp"], Ss(ports));

        var results = await new SyslogNgCheck().RunAsync(TestContext.Build(runner, Spec(ports)));

        var r = Single(results, "rsyslog 비활성");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Warning, r.Severity);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
