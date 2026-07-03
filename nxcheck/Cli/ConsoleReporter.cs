using NxCheck.Core.Model;

namespace NxCheck.Cli;

/// <summary>콘솔 리포터. PASS는 한 줄, FAIL은 사다리를 펼친다(설계 5장).</summary>
public static class ConsoleReporter
{
    public static void Write(IReadOnlyList<CheckResult> results)
    {
        foreach (var group in results.GroupBy(r => r.Module))
        {
            Console.WriteLine($"[{group.Key}]");
            foreach (var r in group)
                WriteOne(r);
            Console.WriteLine();
        }

        var counts = results.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine(
            $"요약: pass {Count(counts, CheckStatus.Pass)} · " +
            $"fail {Count(counts, CheckStatus.Fail)} · " +
            $"skip {Count(counts, CheckStatus.Skip)} · " +
            $"error {Count(counts, CheckStatus.Error)}");
    }

    private static int Count(Dictionary<CheckStatus, int> d, CheckStatus s) => d.TryGetValue(s, out var n) ? n : 0;

    private static void WriteOne(CheckResult r)
    {
        var mark = r.Status switch
        {
            CheckStatus.Pass => "✓",
            CheckStatus.Fail => "✗",
            CheckStatus.Skip => "·",
            CheckStatus.Error => "!",
            _ => "?",
        };
        Console.WriteLine($"  {mark} {r.Item}  [{r.Status.ToString().ToUpperInvariant()}]");

        if (r.Status is CheckStatus.Pass)
            return;

        if (r.Expected is not null || r.Actual is not null)
            Console.WriteLine($"      기대: {r.Expected ?? "-"}   실제: {r.Actual ?? "-"}");

        foreach (var step in r.Ladder)
        {
            var sm = step.Status == CheckStatus.Pass ? "├ OK" : "├ ✗";
            Console.WriteLine($"      {sm} {step.Label}{(step.Detail is null ? "" : "  " + step.Detail)}");
        }

        if (r.Hint is not null)
            Console.WriteLine($"      ⇒ {r.Hint}");
    }
}
