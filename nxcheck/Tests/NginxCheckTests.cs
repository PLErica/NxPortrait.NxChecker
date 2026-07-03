using System.Globalization;
using NxCheck.Core.Checks;
using NxCheck.Core.Model;
using NxCheck.Core.Runners;
using Xunit;

namespace NxCheck.Tests;

public class NginxCheckTests : IDisposable
{
    private readonly string _sites = Path.Combine(Path.GetTempPath(), $"nxcheck-sites-{Guid.NewGuid():N}");

    public NginxCheckTests() => Directory.CreateDirectory(_sites);

    public void Dispose()
    {
        if (Directory.Exists(_sites)) Directory.Delete(_sites, recursive: true);
    }

    private static string SsTcp(params int[] ports) =>
        "State Recv-Q Send-Q Local Address:Port Peer Address:Port Process\n" + string.Join("\n",
            ports.Select(p => $"LISTEN 0 511 0.0.0.0:{p} 0.0.0.0:* users:((\"nginx\",pid=1,fd=6))"));

    private const string DashTWithCert = """
        http {
            server {
                listen 443 ssl;
                ssl_certificate     /etc/ssl/site.pem;
                ssl_certificate_key /etc/ssl/site.key;
            }
        }
        """;

    private static string NotAfter(int daysFromNow) =>
        "notAfter=" + DateTime.UtcNow.AddDays(daysFromNow).ToString("MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture) + " GMT";

    private FakeCommandRunner Runner(string ssTcp, string dashT, string opensslOut) => new FakeCommandRunner()
        .WhenStdout("systemctl", ["is-active"], "active")
        .WhenStdout("systemctl", ["is-enabled"], "enabled")
        .WhenStdout("nginx", ["-t"], "")
        .WhenStdout("nginx", ["-T"], dashT)
        .WhenStdout("ss", ["-tlnp"], ssTcp)
        .WhenStdout("openssl", ["-enddate"], opensslOut);

    private NginxCheck Check() => new(_sites, certExpiryWarnDays: 30);

    private void WriteSite(string name) => File.WriteAllText(Path.Combine(_sites, name), "server {}");

    [Fact]
    public async Task Healthy_nginx_passes()
    {
        WriteSite("example.conf");
        var results = await Check().RunAsync(
            TestContext.Build(Runner(SsTcp(80, 443), DashTWithCert, NotAfter(200)), new()));

        Assert.Equal(CheckStatus.Pass, Single(results, "active").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "nginx -t").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "80/443 listen").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "TLS 만료 site.pem").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "dangling 링크").Status);
        Assert.Equal(CheckStatus.Pass, Single(results, "default 사이트").Status);
    }

    [Fact]
    public async Task Nginx_test_failure_fails()
    {
        var runner = Runner(SsTcp(80, 443), DashTWithCert, NotAfter(200));
        // -t 실패를 앞에 등록(순서 우선)
        var failing = new FakeCommandRunner()
            .When("nginx", ["-t"], new CommandResult { FileName = "nginx", Arguments = "", Started = true, ExitCode = 1, StdErr = "conf error" })
            .WhenStdout("systemctl", ["is-active"], "active")
            .WhenStdout("systemctl", ["is-enabled"], "enabled")
            .WhenStdout("nginx", ["-T"], DashTWithCert)
            .WhenStdout("ss", ["-tlnp"], SsTcp(80, 443))
            .WhenStdout("openssl", ["-enddate"], NotAfter(200));

        var results = await Check().RunAsync(TestContext.Build(failing, new()));

        Assert.Equal(CheckStatus.Fail, Single(results, "nginx -t").Status);
    }

    [Fact]
    public async Task Missing_listen_port_fails()
    {
        WriteSite("example.conf");
        var results = await Check().RunAsync(
            TestContext.Build(Runner(SsTcp(80), DashTWithCert, NotAfter(200)), new())); // 443 없음
        var r = Single(results, "80/443 listen");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("443", r.Actual);
    }

    [Fact]
    public async Task Expired_cert_is_critical()
    {
        var results = await Check().RunAsync(
            TestContext.Build(Runner(SsTcp(80, 443), DashTWithCert, NotAfter(-5)), new()));
        var r = Single(results, "TLS 만료 site.pem");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Critical, r.Severity);
    }

    [Fact]
    public async Task Soon_expiring_cert_is_warning()
    {
        var results = await Check().RunAsync(
            TestContext.Build(Runner(SsTcp(80, 443), DashTWithCert, NotAfter(10)), new())); // 30일 이내
        var r = Single(results, "TLS 만료 site.pem");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Severity.Warning, r.Severity);
    }

    [Fact]
    public async Task Default_site_present_warns()
    {
        WriteSite("default");
        var results = await Check().RunAsync(
            TestContext.Build(Runner(SsTcp(80, 443), DashTWithCert, NotAfter(200)), new()));
        Assert.Equal(CheckStatus.Fail, Single(results, "default 사이트").Status);
    }

    [Fact]
    public async Task No_ssl_certificate_skips_tls()
    {
        WriteSite("example.conf");
        var results = await Check().RunAsync(
            TestContext.Build(Runner(SsTcp(80, 443), "http { server { listen 80; } }", NotAfter(200)), new()));
        Assert.Equal(CheckStatus.Skip, Single(results, "TLS 인증서").Status);
    }

    private static CheckResult Single(IReadOnlyList<CheckResult> results, string item) =>
        Assert.Single(results, r => r.Item == item);
}
