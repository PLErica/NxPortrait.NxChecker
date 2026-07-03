using System.Diagnostics;
using NxCheck.Core.Runners;
using Xunit;

namespace NxCheck.Tests;

public class CommandRunnerTests
{
    [Fact]
    public async Task Successful_command_captures_stdout()
    {
        var runner = new CommandRunner();
        // dotnet은 Windows·Ubuntu 양쪽에 존재(테스트 실행 전제).
        var result = await runner.RunAsync("dotnet", ["--version"]);

        Assert.True(result.Started);
        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdOut));
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task Missing_tool_reports_not_started()
    {
        var runner = new CommandRunner();
        var result = await runner.RunAsync("nxcheck-no-such-tool-xyz", []);

        Assert.False(result.Started);
        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task Timeout_kills_process_and_flags_timedout()
    {
        var runner = new CommandRunner();
        var (file, args) = OperatingSystem.IsWindows()
            ? ("ping", new[] { "-n", "10", "127.0.0.1" })
            : ("sleep", new[] { "10" });

        var sw = Stopwatch.StartNew();
        var result = await runner.RunAsync(file, args, timeout: TimeSpan.FromMilliseconds(500));
        sw.Stop();

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
        Assert.False(result.Success);
        // 10초짜리 명령이 0.5초 타임아웃에 죽었어야 함(여유 두고 5초 미만).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"실제 경과 {sw.Elapsed}");
    }
}
