using System.Text.Json;
using System.Text.Json.Serialization;
using NxCheck.Core.Model;

namespace NxCheck.Cli;

/// <summary>JSON 리포터. 파이프라인 연동용.</summary>
public static class JsonReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(IReadOnlyList<CheckResult> results)
    {
        var payload = new
        {
            exitCode = ExitCodePolicy.Aggregate(results),
            summary = new
            {
                pass = results.Count(r => r.Status == CheckStatus.Pass),
                fail = results.Count(r => r.Status == CheckStatus.Fail),
                skip = results.Count(r => r.Status == CheckStatus.Skip),
                error = results.Count(r => r.Status == CheckStatus.Error),
            },
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, Options));
    }
}
