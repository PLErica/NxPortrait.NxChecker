namespace NxCheck.Core.Checks.Support;

/// <summary>/proc/net/bonding/&lt;bond&gt; 파싱 결과.</summary>
public sealed record BondInfo(string? Mode, IReadOnlyList<BondSlave> Slaves);

/// <param name="Name">슬레이브 인터페이스명.</param>
/// <param name="Up">MII Status가 up인지.</param>
public readonly record struct BondSlave(string Name, bool Up);

/// <summary>
/// /proc/net/bonding/&lt;bond&gt; 파서. 최상단의 본딩 자체 MII Status는 무시하고,
/// "Slave Interface:" 뒤에 처음 나오는 "MII Status:"만 그 슬레이브의 상태로 잡는다.
/// </summary>
public static class BondingParser
{
    public static BondInfo Parse(string text)
    {
        string? mode = null;
        var slaves = new List<BondSlave>();
        string? current = null;

        foreach (var raw in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (line.StartsWith("Bonding Mode:", StringComparison.Ordinal))
                mode = line["Bonding Mode:".Length..].Trim();
            else if (line.StartsWith("Slave Interface:", StringComparison.Ordinal))
                current = line["Slave Interface:".Length..].Trim();
            else if (line.StartsWith("MII Status:", StringComparison.Ordinal) && current is not null)
            {
                var up = line["MII Status:".Length..].Trim().Equals("up", StringComparison.OrdinalIgnoreCase);
                slaves.Add(new BondSlave(current, up));
                current = null;
            }
        }

        return new BondInfo(mode, slaves);
    }
}
