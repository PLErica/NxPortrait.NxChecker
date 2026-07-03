using System.Text.RegularExpressions;

namespace NxCheck.Core.Checks.Support;

/// <summary>
/// `syslog-ng-ctl stats` CSV 파서 (B — 애플리케이션 레벨 수신·드랍).
/// 형식: SourceName;SourceId;SourceInstance;State;Type;Number
/// SourceInstance의 "...:PORT)"로 포트를 잡고, Type이 processed/dropped인 Number를 누적.
/// 포트당 데몬 1개(인스턴스=포트) 토폴로지면 자연히 포트별로 분리된다.
/// </summary>
public static partial class SyslogNgStatsParser
{
    public static IReadOnlyDictionary<int, DropSample> Parse(string csv)
    {
        var received = new Dictionary<int, long>();
        var dropped = new Dictionary<int, long>();

        foreach (var raw in csv.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var f = raw.Split(';');
            if (f.Length < 6) continue;

            var portMatch = PortRegex().Match(f[2]);
            if (!portMatch.Success || !int.TryParse(portMatch.Groups[1].Value, out var port)) continue;
            if (!long.TryParse(f[5].Trim(), out var number)) continue;

            switch (f[4].Trim())
            {
                case "processed": Add(received, port, number); break;
                case "dropped": Add(dropped, port, number); break;
            }
        }

        var ports = received.Keys.Union(dropped.Keys);
        return ports.ToDictionary(p => p, p => new DropSample(Get(received, p), Get(dropped, p)));
    }

    private static void Add(Dictionary<int, long> d, int key, long v) => d[key] = Get(d, key) + v;
    private static long Get(Dictionary<int, long> d, int key) => d.TryGetValue(key, out var v) ? v : 0;

    // "afsocket_sd.(dgram,AF_INET(0.0.0.0:5140))" → 5140
    [GeneratedRegex(@":(\d+)\)")]
    private static partial Regex PortRegex();
}
