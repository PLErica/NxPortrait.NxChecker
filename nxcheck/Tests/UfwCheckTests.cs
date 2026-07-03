using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class UfwCheckTests : IDisposable
{
    private readonly string _beforeRules = Path.Combine(Path.GetTempPath(), $"nxcheck-before-{Guid.NewGuid():N}.rules");

    public void Dispose()
    {
        if (File.Exists(_beforeRules)) File.Delete(_beforeRules);
    }

    private const string ActiveStatus = """
        Status: active
        Default: deny (incoming), allow (outgoing), disabled (routed)

        To                         Action      From
        22/tcp                     ALLOW IN    Anywhere
        5140:5144/udp              ALLOW IN    Anywhere
        """;

    private FakeCommandRunner Runner(string status) => new FakeCommandRunner()
        .WhenStdout("systemctl", ["is-enabled"], "enabled")
        .WhenStdout("ufw", ["status"], status);

    private void WriteEcho(string action) =>
        File.WriteAllText(_beforeRules, $"-A ufw-before-input -p icmp --icmp-type echo-request -j {action}\n");

    private static ExpectedSpec Spec(int[]? syslog = null, int[]? allowed = null, bool requireIcmp = true) =>
        new()
        {
            Syslog = syslog is null ? null : new SyslogSpec { Ports = [.. syslog] },
            Ufw = new UfwSpec { AllowedPorts = allowed is null ? null : [.. allowed], RequireIcmpDrop = requireIcmp },
        };

    [Fact]
    public async Task Healthy_firewall_passes()
    {
        WriteEcho("DROP");
        var ctx = TestContext.Build(Runner(ActiveStatus), Spec(syslog: [5140, 5141, 5142, 5143, 5144]));

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        Assert.Equal(CheckStatus.Pass, Single(results, "active").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "enabled(부팅)").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "default 정책").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "syslog 포트 허용").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "ICMP ping drop").Status);
    }

    [Fact]
    public async Task Blocked_syslog_port_fails()
    {
        WriteEcho("DROP");
        // 5199는 허용 목록에 없음
        var ctx = TestContext.Build(Runner(ActiveStatus), Spec(syslog: [5140, 5199]));

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        var r = Single(results, "syslog 포트 허용");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("5199", r.Actual);
    }

    [Fact]
    public async Task Drift_detects_unexpected_open_port()
    {
        WriteEcho("DROP");
        // 기대 허용은 5140~5144만 — status의 22는 drift
        var ctx = TestContext.Build(Runner(ActiveStatus), Spec(allowed: [5140, 5141, 5142, 5143, 5144]));

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        var r = Single(results, "포트 drift");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Warning, r.Severity);
        Assert.Contains("22", r.Actual);
    }

    [Fact]
    public async Task Icmp_accept_fails()
    {
        WriteEcho("ACCEPT");
        var ctx = TestContext.Build(Runner(ActiveStatus), Spec());

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        Assert.Equal(CheckStatus.Fail, Single(results, "ICMP ping drop").Status);
    }

    [Fact]
    public async Task Icmp_skipped_when_not_required()
    {
        // 파일 안 만들어도 require=false면 skip
        var ctx = TestContext.Build(Runner(ActiveStatus), Spec(requireIcmp: false));

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        Assert.Equal(CheckStatus.Skip, Single(results, "ICMP ping drop").Status);
    }

    [Fact]
    public async Task Icmp_missing_file_errors()
    {
        var ctx = TestContext.Build(Runner(ActiveStatus), Spec()); // before.rules 없음

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        Assert.Equal(CheckStatus.Error, Single(results, "ICMP ping drop").Status);
    }

    [Fact]
    public async Task Inactive_firewall_fails_active()
    {
        WriteEcho("DROP");
        var ctx = TestContext.Build(Runner("Status: inactive"), Spec());

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        var active = Single(results, "active");
        Assert.Equal(CheckStatus.Fail, active.Status);
        Assert.Equal(Severity.Critical, active.Severity);
    }

    [Fact]
    public async Task Ufw_tool_absent_errors()
    {
        WriteEcho("DROP");
        // ufw 미설정 → status 명령 시작 실패
        var runner = new FakeCommandRunner().WhenStdout("systemctl", ["is-enabled"], "enabled");
        var ctx = TestContext.Build(runner, Spec());

        var results = await new UfwCheck(_beforeRules).RunAsync(ctx);

        Assert.Equal(CheckStatus.Error, Single(results, "status").Status);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
