using NxCheck.Core.Checks.Support;
using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.5 nginx.
/// - nginx -t 문법 · active + enabled
/// - 80/443 listen (ss -tlnp)
/// - TLS 인증서 만료 N일 이상 (nginx -T의 ssl_certificate → openssl enddate)
/// - sites-enabled dangling 심볼릭링크 없음 · default 사이트 없음
/// stream 포트 분배 정합은 4.8 crosscheck.
/// </summary>
public sealed class NginxCheck(
    string sitesEnabledDir = "/etc/nginx/sites-enabled",
    int certExpiryWarnDays = 30) : ICheck
{
    public string Module => "nginx";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var probe = new SystemdProbe(ctx.Runner);
        var results = new List<CheckResult>();

        // active / enabled
        var active = await probe.IsActiveAsync("nginx", ct);
        results.Add(ServiceResult("active", active, Severity.Critical));
        var enabled = await probe.IsEnabledAsync("nginx", ct);
        results.Add(ServiceResult("enabled", enabled, Severity.Warning));

        // nginx -t
        var t = await ctx.Runner.RunAsync("nginx", ["-t"], ct: ct);
        results.Add(!t.Started
            ? CheckResult.Error(Module, "nginx -t", Severity.Critical, hint: "nginx 실행 실패")
            : t.Success
                ? CheckResult.Pass(Module, "nginx -t", Severity.Critical, expected: "통과", actual: "통과")
                : CheckResult.Fail(Module, "nginx -t", Severity.Critical, expected: "통과",
                    actual: $"exit {t.ExitCode}", hint: t.StdErr.Trim()));

        // 80/443 listen
        results.Add(await CheckListenAsync(ctx, ct));

        // TLS 인증서 만료 (nginx -T 필요)
        var dashT = await ctx.Runner.RunAsync("nginx", ["-T"], ct: ct);
        if (dashT is { Started: true, Success: true })
            results.AddRange(await CheckCertsAsync(ctx, dashT.StdOut, ct));
        else
            results.Add(CheckResult.Skip(Module, "TLS 인증서", hint: "nginx -T 실패 — 인증서 경로 미확보"));

        // sites-enabled 위생
        results.AddRange(CheckSitesEnabled());

        return results;
    }

    private CheckResult ServiceResult(string item, ServiceState st, Severity sev) =>
        !st.Queried ? CheckResult.Error(Module, item, sev, hint: "systemctl 실행 실패")
        : st.Ok ? CheckResult.Pass(Module, item, sev, expected: item, actual: st.Raw)
        : CheckResult.Fail(Module, item, sev, expected: item, actual: st.Raw);

    private async Task<CheckResult> CheckListenAsync(CheckContext ctx, CancellationToken ct)
    {
        var ss = await ctx.Runner.RunAsync("ss", ["-tlnp"], ct: ct);
        if (!ss.Started)
            return CheckResult.Error(Module, "80/443 listen", Severity.Critical, hint: $"ss 실패: {ss.FailureReason}");

        var ports = SsParser.TcpListenerPorts(ss.StdOut, processContains: "nginx");
        var missing = new[] { 80, 443 }.Where(p => !ports.Contains(p)).ToList();

        return missing.Count == 0
            ? CheckResult.Pass(Module, "80/443 listen", Severity.Critical, expected: "80,443", actual: "listen 중")
            : CheckResult.Fail(Module, "80/443 listen", Severity.Critical,
                expected: "80,443", actual: $"미listen: {string.Join(",", missing)}",
                hint: "nginx가 해당 포트를 열지 않음");
    }

    private async Task<IReadOnlyList<CheckResult>> CheckCertsAsync(CheckContext ctx, string dashT, CancellationToken ct)
    {
        var paths = NginxConfParser.SslCertificatePaths(dashT);
        if (paths.Count == 0)
            return [CheckResult.Skip(Module, "TLS 인증서", hint: "ssl_certificate 지시문 없음(HTTP 전용?)")];

        var results = new List<CheckResult>();
        foreach (var path in paths)
        {
            var item = $"TLS 만료 {Path.GetFileName(path)}";
            var r = await ctx.Runner.RunAsync("openssl", ["x509", "-enddate", "-noout", "-in", path], ct: ct);
            if (!r.Started || !r.Success)
            {
                results.Add(CheckResult.Fail(Module, item, Severity.Critical,
                    expected: "인증서 읽기 가능", actual: r.Started ? $"openssl exit {r.ExitCode}" : "openssl 실행 실패",
                    hint: "인증서 파일이 없거나 읽을 수 없음"));
                continue;
            }

            var notAfter = NginxConfParser.ParseNotAfter(r.StdOut);
            if (notAfter is not { } expiry)
            {
                results.Add(CheckResult.Error(Module, item, Severity.Warning, hint: "notAfter 파싱 실패"));
                continue;
            }

            var days = (int)(expiry - DateTime.UtcNow).TotalDays;
            results.Add(days switch
            {
                < 0 => CheckResult.Fail(Module, item, Severity.Critical,
                    expected: $"{certExpiryWarnDays}일 이상", actual: $"{-days}일 전 만료", hint: "인증서 만료됨"),
                _ when days < certExpiryWarnDays => CheckResult.Fail(Module, item, Severity.Warning,
                    expected: $"{certExpiryWarnDays}일 이상", actual: $"{days}일 남음", hint: "인증서 곧 만료 — 갱신 필요"),
                _ => CheckResult.Pass(Module, item, Severity.Warning,
                    expected: $"{certExpiryWarnDays}일 이상", actual: $"{days}일 남음"),
            });
        }
        return results;
    }

    private IReadOnlyList<CheckResult> CheckSitesEnabled()
    {
        if (!Directory.Exists(sitesEnabledDir))
            return [CheckResult.Skip(Module, "sites-enabled", hint: $"{sitesEnabledDir} 없음")];

        var entries = Directory.GetFileSystemEntries(sitesEnabledDir);
        var results = new List<CheckResult>();

        // dangling 심볼릭링크
        var dangling = entries
            .Select(e => (Entry: e, Target: File.ResolveLinkTarget(e, returnFinalTarget: true)))
            .Where(x => x.Target is { Exists: false })
            .Select(x => Path.GetFileName(x.Entry))
            .ToList();

        results.Add(dangling.Count == 0
            ? CheckResult.Pass(Module, "dangling 링크", Severity.Warning, expected: "없음", actual: "없음")
            : CheckResult.Fail(Module, "dangling 링크", Severity.Warning,
                expected: "없음", actual: string.Join(",", dangling),
                hint: "대상이 사라진 심볼릭링크 — sites-available에서 제거됨"));

        // default 사이트
        var hasDefault = entries.Any(e => string.Equals(Path.GetFileName(e), "default", StringComparison.Ordinal));
        results.Add(hasDefault
            ? CheckResult.Fail(Module, "default 사이트", Severity.Warning,
                expected: "없음", actual: "default 존재", hint: "불필요한 기본 사이트 — 비활성 권장")
            : CheckResult.Pass(Module, "default 사이트", Severity.Warning, expected: "없음", actual: "없음"));

        return results;
    }
}
