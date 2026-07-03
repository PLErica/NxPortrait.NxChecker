using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class ElasticsearchCheckTests : IDisposable
{
    private readonly string _jvm = Path.Combine(Path.GetTempPath(), $"nxcheck-jvm-{Guid.NewGuid():N}.options");

    public void Dispose()
    {
        if (File.Exists(_jvm)) File.Delete(_jvm);
    }

    private void WriteJvm(string content) => File.WriteAllText(_jvm, content);

    private static string Health(string status, int nodes) =>
        "epoch timestamp cluster status node.total node.data shards\n" +
        $"1620 12:00:00 nxcluster {status} {nodes} {nodes} 10";

    private static string Alloc(int diskPercent) =>
        "shards disk.indices disk.used disk.avail disk.total disk.percent host ip node\n" +
        $"10 1gb 40gb 60gb 100gb {diskPercent} 10.0.0.5 10.0.0.5 node1";

    private static ExpectedSpec Spec(int nodes = 3) =>
        new() { Elasticsearch = new ElasticsearchSpec { Url = "https://es:44371", Nodes = nodes } };

    private FakeCommandRunner Runner(string health, string alloc) => new FakeCommandRunner()
        .WhenStdout("systemctl", ["is-active"], "active")
        .WhenStdout("systemctl", ["is-enabled"], "enabled")
        .WhenStdout("curl", ["_cat/health"], health)
        .WhenStdout("curl", ["_cat/allocation"], alloc);

    private ElasticsearchCheck Check() => new(_jvm);

    [Fact]
    public async Task Healthy_cluster_passes()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("green", 3), Alloc(40)), Spec()));

        Assert.Equal(CheckStatus.Pass, Single(results, "active").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "heap config").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "클러스터 상태").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "디스크 watermark").Status);
    }

    [Fact]
    public async Task Yellow_is_warning()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("yellow", 3), Alloc(40)), Spec()));
        var r = Single(results, "클러스터 상태");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Warning, r.Severity);
    }

    [Fact]
    public async Task Red_is_critical()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("red", 3), Alloc(40)), Spec()));
        var r = Single(results, "클러스터 상태");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Critical, r.Severity);
    }

    [Fact]
    public async Task Node_count_mismatch_fails()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("green", 2), Alloc(40)), Spec(nodes: 3)));
        var r = Single(results, "클러스터 상태");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("2", r.Actual);
    }

    [Fact]
    public async Task Asymmetric_heap_fails()
    {
        WriteJvm("-Xms8g\n-Xmx16g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("green", 3), Alloc(40)), Spec()));
        Assert.Equal(CheckStatus.Fail, Single(results, "heap config").Status);
    }

    [Fact]
    public async Task Oversized_heap_fails()
    {
        WriteJvm("-Xms32g\n-Xmx32g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("green", 3), Alloc(40)), Spec()));
        var r = Single(results, "heap config");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("31GB", r.Expected);
    }

    [Fact]
    public async Task Disk_flood_is_critical()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        var results = await Check().RunAsync(TestContext.Build(Runner(Health("green", 3), Alloc(96)), Spec()));
        var r = Single(results, "디스크 watermark");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Critical, r.Severity);
    }

    [Fact]
    public async Task Health_query_failure_errors()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        // curl 미설정 → health 조회 시작 실패
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("systemctl", ["is-enabled"], "enabled");
        var results = await Check().RunAsync(TestContext.Build(runner, Spec()));
        Assert.Equal(CheckStatus.Error, Single(results, "클러스터 상태").Status);
    }

    [Fact]
    public async Task Url_absent_skips_cluster()
    {
        WriteJvm("-Xms16g\n-Xmx16g");
        var runner = new FakeCommandRunner()
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("systemctl", ["is-enabled"], "enabled");
        var results = await Check().RunAsync(TestContext.Build(runner, new ExpectedSpec()));
        Assert.Equal(CheckStatus.Skip, Single(results, "클러스터 상태").Status);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
