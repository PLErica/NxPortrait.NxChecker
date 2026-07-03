using NxCheck.Core.Checks;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class CrossCheckTests
{
    private static string Nginx(params int[] ports) =>
        "stream {\n" + string.Join("\n",
            ports.Select(p => $"  upstream u_{p} {{ server 127.0.0.1:{p}; }}")) + "\n}";

    private static string Ss(params int[] ports) =>
        "State Recv-Q Send-Q Local Address:Port Peer Address:Port Process\n" + string.Join("\n",
            ports.Select(p => $"UNCONN 0 0 0.0.0.0:{p} 0.0.0.0:* users:((\"syslog-ng\",pid=1,fd=5))"));

    private static FakeCommandRunner Runner(string nginxOut, string ssOut) => new FakeCommandRunner()
        .WhenStdout("nginx", ["-T"], nginxOut)
        .WhenStdout("ss", ["-ulnp"], ssOut);

    private static async Task<CheckResult> Run(FakeCommandRunner runner) =>
        (await new CrossCheck().RunAsync(TestContext.Build(runner))).Single();

    [Fact]
    public async Task Matching_sets_pass()
    {
        var r = await Run(Runner(Nginx(5140, 5141, 5142), Ss(5140, 5141, 5142)));
        Assert.Equal(CheckStatus.Pass, r.Status);
    }

    [Fact]
    public async Task Detects_off_by_one_not_count_equal()
    {
        // 갯수는 3=3이지만 5142(유실) vs 5199(고아)
        var r = await Run(Runner(Nginx(5140, 5141, 5142), Ss(5140, 5141, 5199)));
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains(r.Ladder, s => s.Label == "5142" && s.Detail!.Contains("유실"));
        Assert.Contains(r.Ladder, s => s.Label == "5199" && s.Detail!.Contains("송신자 없음"));
    }

    [Fact]
    public async Task Missing_listener_is_log_loss()
    {
        var r = await Run(Runner(Nginx(5140, 5141), Ss(5140)));
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains(r.Ladder, s => s.Label == "5141" && s.Detail!.Contains("유실"));
    }

    [Fact]
    public async Task Nginx_config_failure_errors()
    {
        var runner = new FakeCommandRunner()
            .When("nginx", ["-T"], new NxCheck.Core.Runners.CommandResult
            {
                FileName = "nginx", Arguments = "", Started = true, ExitCode = 1, StdErr = "syntax error",
            })
            .WhenStdout("ss", ["-ulnp"], Ss(5140));
        var r = await Run(runner);
        Assert.Equal(CheckStatus.Error, r.Status);
        Assert.Contains("선행", r.Hint);
    }

    [Fact]
    public async Task Ss_absent_errors()
    {
        var runner = new FakeCommandRunner().WhenStdout("nginx", ["-T"], Nginx(5140)); // ss 미설정
        var r = await Run(runner);
        Assert.Equal(CheckStatus.Error, r.Status);
    }

    [Fact]
    public async Task No_stream_ports_skips()
    {
        var r = await Run(Runner("http { }", Ss(5140)));
        Assert.Equal(CheckStatus.Skip, r.Status);
    }
}
