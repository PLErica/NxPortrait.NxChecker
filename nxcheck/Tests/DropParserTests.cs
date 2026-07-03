using NxCheck.Core.Checks.Support;
using Xunit;

namespace NxCheck.Tests;

public class SyslogNgStatsParserTests
{
    private const string Csv = """
        SourceName;SourceId;SourceInstance;State;Type;Number
        source;s_net#0;afsocket_sd.(dgram,AF_INET(0.0.0.0:5140));a;processed;1000
        source;s_net#0;afsocket_sd.(dgram,AF_INET(0.0.0.0:5140));a;dropped;12
        source;s_net#1;afsocket_sd.(dgram,AF_INET(0.0.0.0:5141));a;processed;2000
        destination;d_es;;a;processed;9999
        """;

    [Fact]
    public void Parses_processed_and_dropped_per_port()
    {
        var map = SyslogNgStatsParser.Parse(Csv);
        Assert.Equal(1000, map[5140].Received);
        Assert.Equal(12, map[5140].Drops);
        Assert.Equal(2000, map[5141].Received);
        Assert.Equal(0, map[5141].Drops);
    }

    [Fact]
    public void Ignores_lines_without_port() =>
        Assert.DoesNotContain(SyslogNgStatsParser.Parse(Csv), kv => kv.Value.Received == 9999);
}

public class ProcNetUdpParserTests
{
    // 0x1414 = 5140, 0x1415 = 5141. drops는 마지막 컬럼.
    private const string Proc = """
          sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode ref pointer drops
           0: 00000000:1414 00000000:0000 07 00000000:00000000 00:00000000 00000000 0 0 12345 2 0000000000000000 4690
           1: 00000000:1415 00000000:0000 07 00000000:00000000 00:00000000 00000000 0 0 12346 2 0000000000000000 0
        """;

    [Fact]
    public void Parses_hex_port_and_drops()
    {
        var drops = ProcNetUdpParser.SocketDrops(Proc);
        Assert.Equal(4690, drops[5140]);
        Assert.Equal(0, drops[5141]);
    }

    [Fact]
    public void Sums_drops_for_same_port()
    {
        var text = Proc + "\n   2: 00000000:1414 00000000:0000 07 00000000:00000000 00:00000000 00000000 0 0 9 2 0 10";
        var drops = ProcNetUdpParser.SocketDrops(text);
        Assert.Equal(4700, drops[5140]);
    }
}
