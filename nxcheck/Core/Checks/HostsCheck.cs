using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.1 hosts — /etc/hosts 정합. (다른 모듈 구현의 패턴 예시 역할)
/// - 127.0.0.1 localhost 존재
/// - hostnamectl 호스트명 ↔ hosts 매핑 일치
/// - 기대 FQDN 엔트리 존재(expected.hostname 있을 때만)
/// </summary>
public sealed class HostsCheck(string hostsPath = "/etc/hosts") : ICheck
{
    public string Module => "hosts";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        if (!File.Exists(hostsPath))
        {
            results.Add(CheckResult.Error(Module, "/etc/hosts 읽기", Severity.Critical,
                hint: $"{hostsPath} 없음"));
            return results;
        }

        var lines = (await File.ReadAllLinesAsync(hostsPath, ct))
            .Select(StripComment)
            .Where(l => l.Length > 0)
            .ToArray();

        // 127.0.0.1 localhost
        var hasLoopback = lines.Any(l =>
        {
            var t = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return t.Length >= 2 && t[0] == "127.0.0.1" && t.Skip(1).Contains("localhost");
        });
        results.Add(hasLoopback
            ? CheckResult.Pass(Module, "127.0.0.1 localhost", Severity.Critical, expected: "존재", actual: "존재")
            : CheckResult.Fail(Module, "127.0.0.1 localhost", Severity.Critical,
                expected: "127.0.0.1 localhost", actual: "없음",
                hint: "loopback 매핑 누락 — 다수 서비스가 localhost 해석에 의존"));

        // hostnamectl ↔ hosts (기대 호스트명이 있으면 우선 그걸로 검증)
        var expectedHost = ctx.Expected.Hostname;
        var hostnameResult = await ctx.Runner.RunAsync("hostnamectl", ["--static"], ct: ct);
        if (!hostnameResult.Success)
        {
            results.Add(CheckResult.Skip(Module, "호스트명 ↔ hosts 매핑",
                hint: $"hostnamectl 실행 실패: {hostnameResult.FailureReason}"));
        }
        else
        {
            var host = hostnameResult.StdOut.Trim();
            var mapped = lines.Any(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Any(name => name == host || name.StartsWith(host + ".", StringComparison.Ordinal)));
            results.Add(mapped
                ? CheckResult.Pass(Module, "호스트명 ↔ hosts 매핑", Severity.Warning, expected: host, actual: "매핑 존재")
                : CheckResult.Fail(Module, "호스트명 ↔ hosts 매핑", Severity.Warning,
                    expected: $"{host} 엔트리", actual: "없음",
                    hint: "hosts에 현재 호스트명 매핑이 없음"));

            if (expectedHost is not null && !string.Equals(host, expectedHost, StringComparison.Ordinal))
            {
                results.Add(CheckResult.Fail(Module, "기대 호스트명", Severity.Warning,
                    expected: expectedHost, actual: host,
                    hint: "expected.yaml의 hostname과 실제 호스트명 불일치(템플릿 잔재 가능)"));
            }
        }

        return results;
    }

    private static string StripComment(string line)
    {
        var idx = line.IndexOf('#');
        return (idx >= 0 ? line[..idx] : line).Trim();
    }
}
