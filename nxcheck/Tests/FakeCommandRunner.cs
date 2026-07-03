using NxCheck.Core.Runners;

namespace NxCheck.Tests;

/// <summary>
/// 파서·체크 단위 테스트용 가짜 러너. 파일명별로 응답을 미리 박아둔다.
/// 설정 안 된 명령은 "시작 못 함"(도구 부재처럼) 응답 — 의도치 않은 호출이 드러난다.
/// </summary>
public sealed class FakeCommandRunner : ICommandRunner
{
    private readonly Dictionary<string, CommandResult> _byFile = new();

    public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];

    public FakeCommandRunner Set(string fileName, CommandResult result)
    {
        _byFile[fileName] = result;
        return this;
    }

    public FakeCommandRunner SetStdout(string fileName, string stdout, int exitCode = 0) =>
        Set(fileName, new CommandResult
        {
            FileName = fileName,
            Arguments = "",
            Started = true,
            ExitCode = exitCode,
            StdOut = stdout,
        });

    public FakeCommandRunner SetTimedOut(string fileName) =>
        Set(fileName, new CommandResult { FileName = fileName, Arguments = "", Started = true, TimedOut = true });

    public Task<CommandResult> RunAsync(
        string fileName, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Calls.Add((fileName, args));
        var result = _byFile.TryGetValue(fileName, out var r)
            ? r
            : new CommandResult { FileName = fileName, Arguments = string.Join(' ', args), Started = false };
        return Task.FromResult(result);
    }
}
