using System.Text.Json;

namespace NxCheck.Core.Checks.Support;

/// <summary>`ip -j addr` (JSON) 출력 파서.</summary>
public static class IpAddrParser
{
    /// <summary>모든 인터페이스의 IPv4(local) 주소 집합.</summary>
    public static IReadOnlySet<string> IPv4Locals(string json)
    {
        var set = new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return set;

            foreach (var iface in doc.RootElement.EnumerateArray())
            {
                if (!iface.TryGetProperty("addr_info", out var addrs) || addrs.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var a in addrs.EnumerateArray())
                {
                    if (a.TryGetProperty("family", out var fam) && fam.GetString() == "inet"
                        && a.TryGetProperty("local", out var local) && local.GetString() is { } ip)
                        set.Add(ip);
                }
            }
        }
        catch (JsonException)
        {
            // 파싱 실패 → 빈 집합(호출부에서 불일치로 처리)
        }
        return set;
    }
}
