using NxCheck.Core.Checks.Support;
using Xunit;

namespace NxCheck.Tests;

public class UfwStatusParserTests
{
    private const string Verbose = """
        Status: active

        Logging: on (low)
        Default: deny (incoming), allow (outgoing), disabled (routed)
        New profiles: skip

        To                         Action      From
        --                         ------      ----
        22/tcp                     ALLOW IN    Anywhere
        80/tcp                     ALLOW IN    Anywhere
        5140:5144/udp              ALLOW IN    Anywhere
        22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """;

    [Fact]
    public void Parses_active_and_default_policy()
    {
        var s = UfwStatusParser.Parse(Verbose);
        Assert.True(s.Active);
        Assert.Equal("deny", s.DefaultIncoming);
        Assert.Equal("allow", s.DefaultOutgoing);
    }

    [Fact]
    public void Parses_allowed_ports_including_range()
    {
        var s = UfwStatusParser.Parse(Verbose);
        Assert.Contains(22, s.AllowedPorts);
        Assert.Contains(80, s.AllowedPorts);
        foreach (var p in new[] { 5140, 5141, 5142, 5143, 5144 })
            Assert.Contains(p, s.AllowedPorts);
    }

    [Fact]
    public void Inactive_status_parsed()
    {
        var s = UfwStatusParser.Parse("Status: inactive");
        Assert.False(s.Active);
        Assert.Empty(s.AllowedPorts);
    }

    [Theory]
    [InlineData("22/tcp", new[] { 22 })]
    [InlineData("5140:5142/udp", new[] { 5140, 5141, 5142 })]
    [InlineData("Anywhere", new int[0])]
    [InlineData("443", new[] { 443 })]
    public void PortsFromField_cases(string field, int[] expected) =>
        Assert.Equal(expected, UfwStatusParser.PortsFromField(field).ToArray());
}
