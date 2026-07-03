using NxCheck.Core.Checks.Support;
using Xunit;

namespace NxCheck.Tests;

public class NginxStreamParserTests
{
    private const string DashT = """
        http {
            upstream backend {
                server 127.0.0.1:8080;
            }
            server {
                listen 80;
                server_name example.com;
            }
        }
        stream {
            upstream syslog_5140 { server 127.0.0.1:5140; }
            upstream syslog_5141 { server 10.0.0.5:5141; }
            server { listen 5140 udp; proxy_pass syslog_5140; }
            server { listen 5141 udp; proxy_pass syslog_5141; }
        }
        """;

    [Fact]
    public void Extracts_only_stream_upstream_ports()
    {
        var ports = NginxStreamParser.UpstreamPorts(DashT);
        Assert.Equal([5140, 5141], ports.OrderBy(p => p).ToArray());
        Assert.DoesNotContain(8080, ports); // http upstream은 제외
    }

    [Fact]
    public void No_stream_block_returns_empty() =>
        Assert.Empty(NginxStreamParser.UpstreamPorts("http { upstream x { server 1.2.3.4:9000; } }"));

    [Fact]
    public void ExtractBlock_matches_braces()
    {
        var body = NginxStreamParser.ExtractBlock("a { b { c } d } e", "a");
        Assert.Equal(" b { c } d ", body);
    }
}

public class SsParserTests
{
    private const string Ulnp = """
        State  Recv-Q Send-Q Local Address:Port Peer Address:Port Process
        UNCONN 0      0      0.0.0.0:5140       0.0.0.0:*         users:(("syslog-ng",pid=100,fd=5))
        UNCONN 0      0      [::]:5141          [::]:*            users:(("syslog-ng",pid=100,fd=6))
        UNCONN 0      0      127.0.0.53%lo:53   0.0.0.0:*         users:(("systemd-resolve",pid=90,fd=12))
        """;

    [Fact]
    public void Filters_by_process_and_normalizes_v4_v6()
    {
        var ports = SsParser.UdpListenerPorts(Ulnp, processContains: "syslog-ng");
        Assert.Equal([5140, 5141], ports.OrderBy(p => p).ToArray());
        Assert.DoesNotContain(53, ports); // systemd-resolve 제외
    }

    [Fact]
    public void Without_filter_returns_all_local_ports()
    {
        var ports = SsParser.UdpListenerPorts(Ulnp);
        Assert.Contains(53, ports);
        Assert.Contains(5140, ports);
    }
}
