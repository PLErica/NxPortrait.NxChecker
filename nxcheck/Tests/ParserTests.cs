using NxCheck.Core.Checks.Support;
using Xunit;

namespace NxCheck.Tests;

public class IpAddrParserTests
{
    [Fact]
    public void Extracts_ipv4_ignores_ipv6()
    {
        const string json = """
            [
              {"ifname":"lo","addr_info":[{"family":"inet","local":"127.0.0.1","prefixlen":8}]},
              {"ifname":"bond0","addr_info":[
                {"family":"inet","local":"10.0.0.5","prefixlen":24},
                {"family":"inet6","local":"fe80::1","prefixlen":64}]}
            ]
            """;
        var set = IpAddrParser.IPv4Locals(json);
        Assert.Contains("10.0.0.5", set);
        Assert.Contains("127.0.0.1", set);
        Assert.DoesNotContain("fe80::1", set);
    }

    [Fact]
    public void Malformed_json_returns_empty() =>
        Assert.Empty(IpAddrParser.IPv4Locals("not json {["));

    [Fact]
    public void Non_array_returns_empty() =>
        Assert.Empty(IpAddrParser.IPv4Locals("{\"ifname\":\"x\"}"));
}

public class BondingParserTests
{
    private const string Sample = """
        Ethernet Channel Bonding Driver: v5.15.0
        Bonding Mode: IEEE 802.3ad Dynamic link aggregation
        MII Status: up
        MII Polling Interval (ms): 100

        Slave Interface: eno1
        MII Status: up
        Speed: 1000 Mbps

        Slave Interface: eno2
        MII Status: down
        Speed: Unknown
        """;

    [Fact]
    public void Parses_mode_and_slaves()
    {
        var info = BondingParser.Parse(Sample);
        Assert.Contains("802.3ad", info.Mode);
        Assert.Equal(2, info.Slaves.Count);
    }

    [Fact]
    public void Ignores_top_level_mii_and_tracks_slave_status()
    {
        var info = BondingParser.Parse(Sample);
        var eno1 = Assert.Single(info.Slaves, s => s.Name == "eno1");
        var eno2 = Assert.Single(info.Slaves, s => s.Name == "eno2");
        Assert.True(eno1.Up);
        Assert.False(eno2.Up);
    }
}
