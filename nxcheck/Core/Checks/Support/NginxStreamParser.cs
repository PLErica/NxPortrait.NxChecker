using System.Text.RegularExpressions;

namespace NxCheck.Core.Checks.Support;

/// <summary>
/// `nginx -T` 덤프에서 stream 블록 안 upstream server 타깃 포트를 추출.
/// stream 블록을 브레이스 매칭으로 도려낸 뒤 `server host:port;` 라인만 잡는다
/// (http upstream 등 stream 밖 server는 제외).
/// </summary>
public static partial class NginxStreamParser
{
    public static IReadOnlySet<int> UpstreamPorts(string nginxDashT)
    {
        var ports = new HashSet<int>();
        var block = ExtractBlock(nginxDashT, "stream");
        if (block is null)
            return ports;

        foreach (Match m in ServerPortRegex().Matches(block))
            if (int.TryParse(m.Groups[1].Value, out var p))
                ports.Add(p);

        return ports;
    }

    /// <summary>keyword 블록의 중괄호 안 내용을 브레이스 매칭으로 반환(없으면 null).</summary>
    public static string? ExtractBlock(string text, string keyword)
    {
        var open = Regex.Match(text, $@"\b{Regex.Escape(keyword)}\s*\{{");
        if (!open.Success)
            return null;

        var start = open.Index + open.Length - 1; // '{' 위치
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[(start + 1)..i];
        }
        return null; // 닫는 괄호 없음(불완전)
    }

    // "server 127.0.0.1:5140;" / "server [::1]:5140;" → 5140
    [GeneratedRegex(@"\bserver\s+[^;{]*:(\d+)\s*;")]
    private static partial Regex ServerPortRegex();
}
