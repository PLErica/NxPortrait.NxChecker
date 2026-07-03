using NxCheck.Core.Expected;
using Xunit;

namespace NxCheck.Tests;

public class ExpectedSpecLoaderTests
{
    [Fact]
    public void Parse_full_spec()
    {
        const string yaml = """
            hostname: 01.nxportrait
            network:
              ip: 10.0.0.5
              gateway: 10.0.0.1
              bond:
                name: bond0
                mode: 802.3ad
                slaves: [eno1, eno2]
            elasticsearch:
              url: https://00.mxlandscape:44371
              nodes: 3
            syslog:
              ports: [5140, 5141, 5142, 5143, 5144]
            drop:
              warn_pct: 25
              critical_pct: 40
            """;

        var spec = ExpectedSpecLoader.Parse(yaml);

        Assert.Equal("01.nxportrait", spec.Hostname);
        Assert.Equal("10.0.0.5", spec.Network?.Ip);
        Assert.Equal("bond0", spec.Network?.Bond?.Name);
        Assert.Equal(["eno1", "eno2"], spec.Network?.Bond?.Slaves);
        Assert.Equal("https://00.mxlandscape:44371", spec.Elasticsearch?.Url);
        Assert.Equal(3, spec.Elasticsearch?.Nodes);
        Assert.Equal([5140, 5141, 5142, 5143, 5144], spec.Syslog?.Ports);
        Assert.Equal(25, spec.Drop?.WarnPct);
        Assert.Equal(40, spec.Drop?.CriticalPct);
    }

    [Fact]
    public void Missing_sections_are_null()
    {
        var spec = ExpectedSpecLoader.Parse("hostname: only-host");
        Assert.Equal("only-host", spec.Hostname);
        Assert.Null(spec.Network);
        Assert.Null(spec.Elasticsearch);
        Assert.Null(spec.Syslog);
    }

    [Fact]
    public void Empty_yaml_is_empty_spec()
    {
        var spec = ExpectedSpecLoader.Parse("");
        Assert.Null(spec.Hostname);
        Assert.Null(spec.Network);
    }

    [Fact]
    public void Load_missing_file_returns_empty()
    {
        var spec = ExpectedSpecLoader.Load(Path.Combine(Path.GetTempPath(), "nxcheck-no-such-file.yaml"));
        Assert.Null(spec.Hostname);
    }

    [Fact]
    public void Drop_defaults_when_omitted()
    {
        var spec = ExpectedSpecLoader.Parse("drop: {}");
        Assert.Equal(20, spec.Drop?.WarnPct);
        Assert.Equal(30, spec.Drop?.CriticalPct);
        Assert.Equal(100, spec.Drop?.MinSamples);
    }
}
