using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using NxCheck.Core.Runners;
using Xunit;

namespace NxCheck.Tests;

public class NetplanCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nxcheck-netplan-{Guid.NewGuid():N}");
    private readonly string _netplanDir;
    private readonly string _bondingDir;

    public NetplanCheckTests()
    {
        _netplanDir = Path.Combine(_root, "netplan");
        _bondingDir = Path.Combine(_root, "bonding");
        Directory.CreateDirectory(_netplanDir);
        Directory.CreateDirectory(_bondingDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string ValidYaml = """
        network:
          version: 2
          bonds:
            bond0:
              interfaces: [eno1, eno2]
              addresses: [10.0.0.5/24]
        """;

    private const string IpJson = """
        [{"ifname":"bond0","addr_info":[{"family":"inet","local":"10.0.0.5","prefixlen":24}]}]
        """;

    private const string BondUp = """
        Bonding Mode: IEEE 802.3ad Dynamic link aggregation
        MII Status: up

        Slave Interface: eno1
        MII Status: up

        Slave Interface: eno2
        MII Status: up
        """;

    private void WriteYaml(string content) => File.WriteAllText(Path.Combine(_netplanDir, "01-netcfg.yaml"), content);
    private void WriteBond(string content) => File.WriteAllText(Path.Combine(_bondingDir, "bond0"), content);

    private static ExpectedSpec Spec() => new()
    {
        Network = new NetworkSpec
        {
            Ip = "10.0.0.5",
            Gateway = "10.0.0.1",
            Bond = new BondSpec { Name = "bond0", Mode = "802.3ad", Slaves = ["eno1", "eno2"] },
        },
    };

    private FakeCommandRunner Runner(bool pingOk = true)
    {
        var r = new FakeCommandRunner().WhenStdout("ip", ["addr"], IpJson);
        r.When("ping", ["10.0.0.1"], pingOk
            ? FakeCommandRunner.Ok("ping", "1 received")
            : new CommandResult { FileName = "ping", Arguments = "", Started = true, ExitCode = 1 });
        return r;
    }

    private NetplanCheck Check() => new(_netplanDir, _bondingDir);

    [Fact]
    public async Task Healthy_config_passes()
    {
        WriteYaml(ValidYaml);
        WriteBond(BondUp);
        var ctx = TestContext.Build(Runner(), Spec(), depth: CheckDepth.Flow);

        var results = await Check().RunAsync(ctx);

        Assert.Equal(CheckStatus.Pass, Single(results, "netplan 문법").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "실제 IP 일치").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "bonding").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "게이트웨이 ping").Status);
    }

    [Fact]
    public async Task Invalid_yaml_fails_syntax()
    {
        WriteYaml("network: [ unclosed");
        WriteBond(BondUp);
        var ctx = TestContext.Build(Runner(), Spec());

        var results = await Check().RunAsync(ctx);

        Assert.Equal(CheckStatus.Fail, Single(results, "netplan 문법").Status);
    }

    [Fact]
    public async Task Ip_mismatch_fails()
    {
        WriteYaml(ValidYaml);
        WriteBond(BondUp);
        var runner = new FakeCommandRunner()
            .WhenStdout("ip", ["addr"], """[{"ifname":"bond0","addr_info":[{"family":"inet","local":"10.0.0.99","prefixlen":24}]}]""");
        var ctx = TestContext.Build(runner, Spec());

        var results = await Check().RunAsync(ctx);

        var r = Single(results, "실제 IP 일치");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("10.0.0.99", r.Actual);
    }

    [Fact]
    public async Task Missing_bond_file_fails()
    {
        WriteYaml(ValidYaml); // bond 파일 안 만듦
        var ctx = TestContext.Build(Runner(), Spec());

        var results = await Check().RunAsync(ctx);

        Assert.Equal(CheckStatus.Fail, Single(results, "bonding").Status);
    }

    [Fact]
    public async Task Slave_down_fails_bonding()
    {
        WriteYaml(ValidYaml);
        WriteBond("""
            Bonding Mode: IEEE 802.3ad Dynamic link aggregation

            Slave Interface: eno1
            MII Status: up

            Slave Interface: eno2
            MII Status: down
            """);
        var ctx = TestContext.Build(Runner(), Spec());

        var results = await Check().RunAsync(ctx);

        var r = Single(results, "bonding");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("eno2", r.Actual);
    }

    [Fact]
    public async Task Gateway_ping_only_in_flow()
    {
        WriteYaml(ValidYaml);
        WriteBond(BondUp);

        var staticResults = await Check().RunAsync(TestContext.Build(Runner(), Spec(), depth: CheckDepth.Static));
        Assert.DoesNotContain(staticResults, r => r.Item == "게이트웨이 ping");

        var flowResults = await Check().RunAsync(TestContext.Build(Runner(pingOk: false), Spec(), depth: CheckDepth.Flow));
        Assert.Equal(CheckStatus.Fail, Single(flowResults, "게이트웨이 ping").Status);
    }

    [Fact]
    public async Task Missing_netplan_dir_errors()
    {
        var ctx = TestContext.Build(Runner(), Spec());
        var check = new NetplanCheck(Path.Combine(_root, "no-such-dir"), _bondingDir);

        var results = await check.RunAsync(ctx);

        Assert.Equal(CheckStatus.Error, Single(results, "netplan 파일").Status);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
