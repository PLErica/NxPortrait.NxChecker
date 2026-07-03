using System.Text.RegularExpressions;

namespace NxCheck.Core.Checks.Support;

/// <summary>`ss` 출력 파서.</summary>
public static partial class SsParser
{
    /// <summary>
    /// UDP 리스너의 Local 포트 집합. processContains가 주어지면 해당 프로세스 라인만.
    /// IPv4(`0.0.0.0:5140`)·IPv6(`[::]:5140`)·와일드카드(`*:5140`)를 포트로만 정규화 → v4/v6 자동 합쳐짐.
    /// </summary>
    public static IReadOnlySet<int> UdpListenerPorts(string ssOutput, string? processContains = null)
    {
        var ports = new HashSet<int>();

        foreach (var raw in ssOutput.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("State", StringComparison.Ordinal)
                || line.StartsWith("Netid", StringComparison.Ordinal)
                || line.Contains("Local Address", StringComparison.Ordinal))
                continue;

            if (processContains is not null && !line.Contains(processContains, StringComparison.Ordinal))
                continue;

            // Local Address:Port는 첫 번째 ':숫자' 토큰(Peer는 ':*'라 매칭 안 됨).
            foreach (var tok in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = LocalPortRegex().Match(tok);
                if (m.Success)
                {
                    ports.Add(int.Parse(m.Groups[1].Value));
                    break;
                }
            }
        }

        return ports;
    }

    [GeneratedRegex(@":(\d+)$")]
    private static partial Regex LocalPortRegex();
}
