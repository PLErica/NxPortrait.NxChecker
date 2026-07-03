namespace NxCheck.Core.Checks.Support;

/// <summary>`ufw status verbose` 파싱 결과.</summary>
public sealed record UfwStatus(
    bool Active,
    string? DefaultIncoming,
    string? DefaultOutgoing,
    IReadOnlySet<int> AllowedPorts);

/// <summary>
/// `ufw status verbose` 출력 파서. ufw는 machine-readable 출력이 없어 텍스트를 파싱한다
/// (LC_ALL=C 전제). IPv4/IPv6 중복 라인은 포트 집합으로 합쳐지므로 자연히 정규화됨.
/// </summary>
public static class UfwStatusParser
{
    public static UfwStatus Parse(string stdout)
    {
        var active = false;
        string? inc = null, outg = null;
        var allowed = new HashSet<int>();

        foreach (var raw in stdout.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
            {
                // "inactive"가 "active"를 부분 포함하므로 정확 매칭.
                var word = line["Status:".Length..].Trim()
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                active = string.Equals(word, "active", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("Default:", StringComparison.OrdinalIgnoreCase))
            {
                // "Default: deny (incoming), allow (outgoing), disabled (routed)"
                inc = ExtractPolicy(line, "incoming");
                outg = ExtractPolicy(line, "outgoing");
                continue;
            }

            // 규칙 라인: "22/tcp   ALLOW IN   Anywhere"  ("(v6)" 변형 포함)
            if (line.Contains("ALLOW", StringComparison.Ordinal) && line.Contains("IN", StringComparison.Ordinal))
            {
                var field = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (field is not null)
                    foreach (var p in PortsFromField(field))
                        allowed.Add(p);
            }
        }

        return new UfwStatus(active, inc, outg, allowed);
    }

    /// <summary>"deny (incoming)" 같은 조각에서 방향 앞의 정책 단어를 뽑는다.</summary>
    private static string? ExtractPolicy(string line, string direction)
    {
        var idx = line.IndexOf($"({direction})", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var before = line[..idx].TrimEnd().TrimEnd('(').Trim();
        var word = before.Split([' ', ',', '('], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return word?.ToLowerInvariant();
    }

    /// <summary>"5140:5144/udp" → 5140..5144, "22/tcp" → 22, "Anywhere" → 없음.</summary>
    public static IEnumerable<int> PortsFromField(string field)
    {
        var portPart = field.Split('/')[0];

        if (portPart.Contains(':'))
        {
            var bounds = portPart.Split(':');
            if (bounds.Length == 2 && int.TryParse(bounds[0], out var lo) && int.TryParse(bounds[1], out var hi) && lo <= hi)
                for (var p = lo; p <= hi; p++)
                    yield return p;
        }
        else if (int.TryParse(portPart, out var single))
        {
            yield return single;
        }
    }
}
