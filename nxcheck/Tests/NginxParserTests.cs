using NxCheck.Core.Checks.Support;
using Xunit;

namespace NxCheck.Tests;

public class NginxConfParserTests
{
    [Fact]
    public void Extracts_cert_paths_excluding_key()
    {
        const string dashT = """
            server {
                ssl_certificate     /etc/ssl/site.pem;
                ssl_certificate_key /etc/ssl/site.key;
            }
            """;
        var paths = NginxConfParser.SslCertificatePaths(dashT);
        Assert.Equal(["/etc/ssl/site.pem"], paths);
        Assert.DoesNotContain("/etc/ssl/site.key", paths);
    }

    [Fact]
    public void Deduplicates_cert_paths()
    {
        const string dashT = "ssl_certificate /a.pem;\nssl_certificate /a.pem;\nssl_certificate /b.pem;";
        Assert.Equal(["/a.pem", "/b.pem"], NginxConfParser.SslCertificatePaths(dashT));
    }

    [Theory]
    [InlineData("notAfter=Jun 30 12:00:00 2027 GMT", 2027, 6, 30)]
    [InlineData("notAfter=Jun  3 12:00:00 2027 GMT", 2027, 6, 3)] // 공백 패딩 일자
    public void Parses_not_after(string line, int year, int month, int day)
    {
        var dt = NginxConfParser.ParseNotAfter(line);
        Assert.NotNull(dt);
        Assert.Equal(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc), dt!.Value);
    }

    [Fact]
    public void Invalid_not_after_is_null() =>
        Assert.Null(NginxConfParser.ParseNotAfter("notAfter=garbage"));
}
