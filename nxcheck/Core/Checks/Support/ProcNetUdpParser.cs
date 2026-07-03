using System.Globalization;

namespace NxCheck.Core.Checks.Support;

/// <summary>
/// `/proc/net/udp`(+udp6) 파서 (A — 커널 소켓 드랍, 포트별).
/// local_address는 "HEXIP:HEXPORT", drops는 마지막 컬럼. 같은 포트 소켓이 여럿이면 합산.
/// 포트=소켓이라 syslog-ng config와 무관하게 항상 포트별로 나온다.
/// </summary>
public static class ProcNetUdpParser
{
    /// <summary>포트 → 커널 소켓 드랍 카운터.</summary>
    public static IReadOnlyDictionary<int, long> SocketDrops(string procText)
    {
        var drops = new Dictionary<int, long>();

        foreach (var raw in procText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("sl", StringComparison.Ordinal) || line.Contains("local_address", StringComparison.Ordinal))
                continue;

            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 2) continue;

            var colon = f[1].IndexOf(':');
            if (colon < 0) continue;

            var portHex = f[1][(colon + 1)..];
            if (!int.TryParse(portHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port)) continue;
            if (!long.TryParse(f[^1], out var d)) continue;

            drops[port] = (drops.TryGetValue(port, out var cur) ? cur : 0) + d;
        }

        return drops;
    }
}
