using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class DropRateCheckTests
{
    // 포트별 (processed, dropped)로 syslog-ng-ctl stats CSV 한 벌 생성
    private static string Csv(params (int Port, long Proc, long Drop)[] rows) =>
        string.Join("\n", rows.SelectMany(r => new[]
        {
            $"source;s;afsocket_sd.(dgram,AF_INET(0.0.0.0:{r.Port}));a;processed;{r.Proc}",
            $"source;s;afsocket_sd.(dgram,AF_INET(0.0.0.0:{r.Port}));a;dropped;{r.Drop}",
        }));

    private static ExpectedSpec Spec(params int[] ports) => new()
    {
        Syslog = new SyslogSpec { Ports = [.. ports] },
        Drop = new DropSpec { WarnPct = 20, CriticalPct = 30, MinSamples = 100 },
    };

    // 존재하지 않는 proc 경로 → 커널 교차신호는 비어 있음(테스트 관심사 아님)
    private static DropRateCheck Check() => new(procUdpPath: "/nonexistent/proc/net/udp");

    private static CheckContext Flow(FakeCommandRunner r, ExpectedSpec s) =>
        TestContext.Build(r, s, depth: CheckDepth.Flow);

    [Fact]
    public async Task Static_depth_skips()
    {
        var ctx = TestContext.Build(new FakeCommandRunner(), Spec(5140), depth: CheckDepth.Static);
        var results = await Check().RunAsync(ctx);
        Assert.Equal(CheckStatus.Skip, Assert.Single(results).Status);
    }

    [Fact]
    public async Task Classifies_ports_by_delta()
    {
        // t0 → t1 델타: 5140 치명(33%), 5141 경고(25%), 5142 정상(~1%)
        var runner = new FakeCommandRunner().WhenStdoutSequence("syslog-ng-ctl", ["stats"],
            Csv((5140, 1000, 0), (5141, 1000, 0), (5142, 1000, 0)),
            Csv((5140, 2000, 500), (5141, 1750, 250), (5142, 2000, 10)));
        var results = await Check().RunAsync(Flow(runner, Spec(5140, 5141, 5142)));

        Assert.Equal(CheckStatus.Fail, Single(results, "5140 드랍률").Status);
        Assert.Equal(Severity.Critical, Single(results, "5140 드랍률").Severity);
        Assert.Equal(Severity.Warning, Single(results, "5141 드랍률").Severity);
        Assert.Equal(CheckStatus.Pass, Single(results, "5142 드랍률").Status);
    }

    [Fact]
    public async Task Insufficient_traffic_skips()
    {
        var runner = new FakeCommandRunner().WhenStdoutSequence("syslog-ng-ctl", ["stats"],
            Csv((5140, 1000, 0)),
            Csv((5140, 1040, 10))); // 총 50 < 100
        var results = await Check().RunAsync(Flow(runner, Spec(5140)));
        Assert.Equal(CheckStatus.Skip, Single(results, "5140 드랍률").Status);
    }

    [Fact]
    public async Task Counter_reset_skips()
    {
        var runner = new FakeCommandRunner().WhenStdoutSequence("syslog-ng-ctl", ["stats"],
            Csv((5140, 5000, 100)),
            Csv((5140, 1000, 0))); // 재시작 — 카운터 감소
        var results = await Check().RunAsync(Flow(runner, Spec(5140)));
        Assert.Equal(CheckStatus.Skip, Single(results, "5140 드랍률").Status);
    }

    [Fact]
    public async Task Missing_syslogngctl_errors()
    {
        var results = await Check().RunAsync(Flow(new FakeCommandRunner(), Spec(5140)));
        Assert.Equal(CheckStatus.Error, Assert.Single(results).Status);
    }

    [Fact]
    public async Task No_syslog_ports_skips()
    {
        var runner = new FakeCommandRunner().WhenStdout("syslog-ng-ctl", ["stats"], "");
        var results = await Check().RunAsync(TestContext.Build(runner, new ExpectedSpec(), depth: CheckDepth.Flow));
        Assert.Equal(CheckStatus.Skip, Assert.Single(results).Status);
    }

    [Fact]
    public async Task Port_absent_in_stats_skips()
    {
        var runner = new FakeCommandRunner().WhenStdoutSequence("syslog-ng-ctl", ["stats"],
            Csv((5140, 1000, 0)), Csv((5140, 2000, 0)));
        var results = await Check().RunAsync(Flow(runner, Spec(5199))); // 5199는 stats에 없음
        Assert.Equal(CheckStatus.Skip, Single(results, "5199 드랍률").Status);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
