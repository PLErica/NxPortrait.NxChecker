using NxCheck.Core.Runners;

namespace NxCheck.Tests;

/// <summary>
/// 파서·체크 단위 테스트용 가짜 러너.
/// 매칭 우선순위: 인자 매처(When*) → 파일명(Set*) → "시작 못 함"(도구 부재처럼).
/// 마지막 폴백 덕에 의도치 않은 호출이 드러난다.
/// </summary>
public sealed class FakeCommandRunner : ICommandRunner
{
    private readonly Dictionary<string, CommandResult> _byFile = new();
    private readonly List<(Func<string, IReadOnlyList<string>, bool> Match, Func<CommandResult> Provide)> _matchers = [];

    public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];

    public FakeCommandRunner Set(string fileName, CommandResult result)
    {
        _byFile[fileName] = result;
        return this;
    }

    public FakeCommandRunner SetStdout(string fileName, string stdout, int exitCode = 0) =>
        Set(fileName, Ok(fileName, stdout, exitCode));

    public FakeCommandRunner SetTimedOut(string fileName) =>
        Set(fileName, new CommandResult { FileName = fileName, Arguments = "", Started = true, TimedOut = true });

    /// <summary>file 이면서 args에 매칭 인자를 모두 포함할 때의 응답.</summary>
    public FakeCommandRunner When(string fileName, string[] argsContain, CommandResult result)
    {
        _matchers.Add((Matcher(fileName, argsContain), () => result));
        return this;
    }

    public FakeCommandRunner WhenStdout(string fileName, string[] argsContain, string stdout, int exitCode = 0) =>
        When(fileName, argsContain, Ok(fileName, stdout, exitCode));

    /// <summary>연속 호출마다 순서대로 다른 응답(두 시점 샘플 등). 마지막은 반복.</summary>
    public FakeCommandRunner WhenSequence(string fileName, string[] argsContain, params CommandResult[] seq)
    {
        var i = 0;
        _matchers.Add((Matcher(fileName, argsContain), () => seq[Math.Min(i++, seq.Length - 1)]));
        return this;
    }

    public FakeCommandRunner WhenStdoutSequence(string fileName, string[] argsContain, params string[] stdouts) =>
        WhenSequence(fileName, argsContain, [.. stdouts.Select(s => Ok(fileName, s))]);

    // argsContain의 각 항목이 어느 인자에든 부분 문자열로 들어 있으면 매칭
    // (curl의 URL 인자처럼 합성된 인자도 잡기 위함).
    private static Func<string, IReadOnlyList<string>, bool> Matcher(string fileName, string[] argsContain) =>
        (f, a) => f == fileName && argsContain.All(sub => a.Any(arg => arg.Contains(sub, StringComparison.Ordinal)));

    public static CommandResult Ok(string fileName, string stdout, int exitCode = 0) =>
        new() { FileName = fileName, Arguments = "", Started = true, ExitCode = exitCode, StdOut = stdout };

    public static CommandResult NotStarted(string fileName) =>
        new() { FileName = fileName, Arguments = "", Started = false };

    public Task<CommandResult> RunAsync(
        string fileName, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Calls.Add((fileName, args));

        foreach (var (match, provide) in _matchers)
            if (match(fileName, args))
                return Task.FromResult(provide());

        var byFile = _byFile.TryGetValue(fileName, out var r)
            ? r
            : new CommandResult { FileName = fileName, Arguments = string.Join(' ', args), Started = false };
        return Task.FromResult(byFile);
    }
}
