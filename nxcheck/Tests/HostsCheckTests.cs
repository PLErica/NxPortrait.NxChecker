using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using Xunit;

namespace NxCheck.Tests;

public class HostsCheckTests : IDisposable
{
    private readonly string _hostsPath = Path.Combine(Path.GetTempPath(), $"nxcheck-hosts-{Guid.NewGuid():N}");

    private void WriteHosts(string content) => File.WriteAllText(_hostsPath, content);

    public void Dispose()
    {
        if (File.Exists(_hostsPath)) File.Delete(_hostsPath);
    }

    [Fact]
    public async Task All_pass_when_loopback_and_hostname_mapped()
    {
        WriteHosts("""
            127.0.0.1 localhost
            10.0.0.5 01.nxportrait 01.nxportrait.core
            """);
        var runner = new FakeCommandRunner().SetStdout("hostnamectl", "01.nxportrait\n");
        var ctx = TestContext.Build(runner, new ExpectedSpec { Hostname = "01.nxportrait" });

        var results = await new HostsCheck(_hostsPath).RunAsync(ctx);

        Assert.All(results, r => Assert.Equal(CheckStatus.Pass, r.Status));
        Assert.Contains(results, r => r.Item == "127.0.0.1 localhost");
        Assert.Contains(results, r => r.Item == "호스트명 ↔ hosts 매핑");
    }

    [Fact]
    public async Task Fail_when_loopback_missing()
    {
        WriteHosts("10.0.0.5 01.nxportrait");
        var runner = new FakeCommandRunner().SetStdout("hostnamectl", "01.nxportrait");
        var ctx = TestContext.Build(runner, new ExpectedSpec { Hostname = "01.nxportrait" });

        var results = await new HostsCheck(_hostsPath).RunAsync(ctx);

        var loopback = Assert.Single(results, r => r.Item == "127.0.0.1 localhost");
        Assert.Equal(CheckStatus.Fail, loopback.Status);
        Assert.Equal(Severity.Critical, loopback.Severity);
    }

    [Fact]
    public async Task Fail_when_expected_hostname_mismatch()
    {
        WriteHosts("""
            127.0.0.1 localhost
            10.0.0.5 oldname
            """);
        var runner = new FakeCommandRunner().SetStdout("hostnamectl", "oldname");
        var ctx = TestContext.Build(runner, new ExpectedSpec { Hostname = "01.nxportrait" });

        var results = await new HostsCheck(_hostsPath).RunAsync(ctx);

        var mismatch = Assert.Single(results, r => r.Item == "기대 호스트명");
        Assert.Equal(CheckStatus.Fail, mismatch.Status);
        Assert.Equal("01.nxportrait", mismatch.Expected);
        Assert.Equal("oldname", mismatch.Actual);
    }

    [Fact]
    public async Task Error_when_hosts_file_missing()
    {
        var runner = new FakeCommandRunner();
        var ctx = TestContext.Build(runner);

        var results = await new HostsCheck(_hostsPath).RunAsync(ctx); // 파일 안 만듦

        var err = Assert.Single(results);
        Assert.Equal(CheckStatus.Error, err.Status);
        Assert.Equal(Severity.Critical, err.Severity);
    }

    [Fact]
    public async Task Skip_hostname_mapping_when_hostnamectl_unavailable()
    {
        WriteHosts("127.0.0.1 localhost");
        var runner = new FakeCommandRunner(); // hostnamectl 미설정 → 시작 못 함
        var ctx = TestContext.Build(runner);

        var results = await new HostsCheck(_hostsPath).RunAsync(ctx);

        var mapping = Assert.Single(results, r => r.Item == "호스트명 ↔ hosts 매핑");
        Assert.Equal(CheckStatus.Skip, mapping.Status);
    }
}
