using System.Text.RegularExpressions;

namespace NxCheck.Core.Checks.Support;

/// <summary>jvm.options의 힙 설정.</summary>
/// <param name="Xms">-Xms 값(예: "31g"), 없으면 null.</param>
/// <param name="Xmx">-Xmx 값.</param>
public readonly record struct HeapConfig(string? Xms, string? Xmx)
{
    public bool Symmetric => Xms is not null && string.Equals(Xms, Xmx, StringComparison.OrdinalIgnoreCase);

    public long? XmxBytes => JvmHeapParser.ToBytes(Xmx);
}

/// <summary>jvm.options(+jvm.options.d)의 -Xms/-Xmx 파서.</summary>
public static partial class JvmHeapParser
{
    public static HeapConfig Parse(string text)
    {
        string? xms = null, xmx = null;
        foreach (var raw in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith('#')) continue;

            var ms = XmsRegex().Match(line);
            if (ms.Success) xms = ms.Groups[1].Value;
            var mx = XmxRegex().Match(line);
            if (mx.Success) xmx = mx.Groups[1].Value;
        }
        return new HeapConfig(xms, xmx);
    }

    /// <summary>"31g"/"16384m"/"8g" → 바이트. 파싱 실패면 null.</summary>
    public static long? ToBytes(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return null;
        var m = SizeRegex().Match(size.Trim());
        if (!m.Success || !long.TryParse(m.Groups[1].Value, out var n)) return null;

        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "g" => n * 1024L * 1024 * 1024,
            "m" => n * 1024L * 1024,
            "k" => n * 1024L,
            _ => n,
        };
    }

    [GeneratedRegex(@"^-Xms(\S+)")] private static partial Regex XmsRegex();
    [GeneratedRegex(@"^-Xmx(\S+)")] private static partial Regex XmxRegex();
    [GeneratedRegex(@"^(\d+)([gGmMkK]?)$")] private static partial Regex SizeRegex();
}
