using NxCheck.Core.Checks.Support;
using Xunit;

namespace NxCheck.Tests;

public class CatTableParserTests
{
    private const string Health = """
        epoch      timestamp cluster   status node.total node.data shards
        1620000000 12:00:00  nxcluster green  3          3         10
        """;

    [Fact]
    public void Maps_header_to_values()
    {
        var rows = CatTableParser.Parse(Health);
        var row = Assert.Single(rows);
        Assert.Equal("green", row["status"]);
        Assert.Equal("3", row["node.total"]);
    }

    [Fact]
    public void Empty_or_header_only_returns_empty()
    {
        Assert.Empty(CatTableParser.Parse(""));
        Assert.Empty(CatTableParser.Parse("only header line"));
    }
}

public class JvmHeapParserTests
{
    [Fact]
    public void Symmetric_heap()
    {
        var h = JvmHeapParser.Parse("-Xms16g\n-Xmx16g\n# comment");
        Assert.True(h.Symmetric);
        Assert.Equal(16L * 1024 * 1024 * 1024, h.XmxBytes);
    }

    [Fact]
    public void Asymmetric_heap()
    {
        var h = JvmHeapParser.Parse("-Xms8g\n-Xmx16g");
        Assert.False(h.Symmetric);
    }

    [Fact]
    public void Ignores_commented_lines()
    {
        var h = JvmHeapParser.Parse("#-Xms1g\n-Xms4g\n-Xmx4g");
        Assert.Equal("4g", h.Xms);
    }

    [Theory]
    [InlineData("31g", 31L * 1024 * 1024 * 1024)]
    [InlineData("16384m", 16384L * 1024 * 1024)]
    [InlineData("8g", 8L * 1024 * 1024 * 1024)]
    public void ToBytes_units(string size, long expected) =>
        Assert.Equal(expected, JvmHeapParser.ToBytes(size));

    [Fact]
    public void ToBytes_invalid_is_null() => Assert.Null(JvmHeapParser.ToBytes("garbage"));
}
