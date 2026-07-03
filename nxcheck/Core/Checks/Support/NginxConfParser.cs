using System.Globalization;
using System.Text.RegularExpressions;

namespace NxCheck.Core.Checks.Support;

/// <summary>`nginx -T` 덤프 파서(TLS 인증서 경로 등).</summary>
public static partial class NginxConfParser
{
    /// <summary>ssl_certificate 경로들(ssl_certificate_key는 제외). 중복 제거.</summary>
    public static IReadOnlyList<string> SslCertificatePaths(string dashT)
    {
        var paths = new List<string>();
        foreach (Match m in SslCertRegex().Matches(dashT))
        {
            var path = m.Groups[1].Value.Trim();
            if (path.Length > 0 && !paths.Contains(path))
                paths.Add(path);
        }
        return paths;
    }

    /// <summary>`openssl x509 -enddate -noout` 출력("notAfter=Jun 30 12:00:00 2027 GMT")의 만료 시각(UTC).</summary>
    public static DateTime? ParseNotAfter(string opensslOut)
    {
        foreach (var raw in opensslOut.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith("notAfter=", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line["notAfter=".Length..].Trim();
            string[] formats =
            [
                "MMM d HH:mm:ss yyyy 'GMT'",
                "MMM dd HH:mm:ss yyyy 'GMT'",
            ];
            if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt))
                return dt;
        }
        return null;
    }

    // "ssl_certificate /path/cert.pem;" — 뒤에 공백을 요구해 ssl_certificate_key 제외
    [GeneratedRegex(@"\bssl_certificate\s+([^;]+);")]
    private static partial Regex SslCertRegex();
}
