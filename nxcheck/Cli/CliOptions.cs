using NxCheck.Core.Model;

namespace NxCheck.Cli;

/// <summary>CLI 인자 파싱 결과.</summary>
public sealed class CliOptions
{
    public RunMode Mode { get; init; } = RunMode.Once;
    public CheckDepth Depth { get; init; } = CheckDepth.Static;
    public bool Json { get; init; }
    public string ExpectedPath { get; init; } = "expected.yaml";
    public TimeSpan SampleWindow { get; init; } = TimeSpan.FromSeconds(10);
    public bool EnableActiveProbe { get; init; }
    public bool ShowHelp { get; init; }
    public string? Error { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var mode = RunMode.Once;
        var depth = CheckDepth.Static;
        var json = false;
        var expected = "expected.yaml";
        var window = TimeSpan.FromSeconds(10);
        var probe = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--once":
                    mode = RunMode.Once;
                    break;
                case "triage":
                    mode = RunMode.Triage;
                    depth = CheckDepth.Flow; // triage는 깊은 진단
                    break;
                case "--flow":
                    depth = CheckDepth.Flow;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--active-probe":
                    probe = true;
                    break;
                case "--expected":
                    if (++i >= args.Length) return Fail("--expected 뒤에 경로가 필요합니다");
                    expected = args[i];
                    break;
                case "--sample-window":
                    if (++i >= args.Length || !int.TryParse(args[i], out var secs))
                        return Fail("--sample-window 뒤에 초(정수)가 필요합니다");
                    window = TimeSpan.FromSeconds(secs);
                    break;
                case "-h" or "--help":
                    return new CliOptions { ShowHelp = true };
                default:
                    return Fail($"알 수 없는 인자: {a}");
            }
        }

        return new CliOptions
        {
            Mode = mode,
            Depth = depth,
            Json = json,
            ExpectedPath = expected,
            SampleWindow = window,
            EnableActiveProbe = probe,
        };
    }

    private static CliOptions Fail(string msg) => new() { Error = msg };

    public const string HelpText = """
        nxcheck — Ubuntu 서버 출고 설정 점검툴 (read-only)

        사용법:
          nxcheck [--once] [옵션]      한 번 검사 (출고 QA, 기본 static)
          nxcheck triage [옵션]        깊은 진단 (flow 포함)

        옵션:
          --flow                  흐름/드랍률 등 출고 후 깊은 검사 포함
          --json                  결과를 JSON으로 출력
          --expected <경로>       기대값 스펙 파일 (기본: expected.yaml)
          --sample-window <초>    드랍 delta 샘플 윈도우 (기본: 10)
          --active-probe          능동 프로브 허용 (side-effect 주의, 기본 off)
          -h, --help              도움말

        종료코드: 0=전부 통과, 1=경고, 2=치명적
        """;
}
