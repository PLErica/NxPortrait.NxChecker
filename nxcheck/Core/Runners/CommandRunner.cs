using System.Diagnostics;
using System.Text;

namespace NxCheck.Core.Runners;

/// <summary>
/// <see cref="ICommandRunner"/> 기본 구현.
/// - 로케일 고정: LC_ALL=C / LANG=C (번역·포맷 흔들림 방지, 설계 1장)
/// - per-call 타임아웃: 초과 시 프로세스 트리 강제 종료 후 TimedOut=true
/// - stdout/stderr 비동기 수집(데드락 방지)
/// </summary>
public sealed class CommandRunner(TimeSpan? defaultTimeout = null) : ICommandRunner
{
    private readonly TimeSpan _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var argDisplay = string.Join(' ', args);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";

        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!proc.Start())
                return NotStarted(fileName, argDisplay, sw.Elapsed, "프로세스 시작 실패");
        }
        catch (Exception ex)
        {
            return NotStarted(fileName, argDisplay, sw.Elapsed, ex.Message);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? _defaultTimeout);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            return new CommandResult
            {
                FileName = fileName,
                Arguments = argDisplay,
                Started = true,
                TimedOut = true,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString(),
                Elapsed = sw.Elapsed,
            };
        }

        // 비동기 리더가 남은 버퍼를 flush 하도록 대기.
        proc.WaitForExit();

        return new CommandResult
        {
            FileName = fileName,
            Arguments = argDisplay,
            Started = true,
            ExitCode = proc.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
            Elapsed = sw.Elapsed,
        };
    }

    private static CommandResult NotStarted(string file, string args, TimeSpan elapsed, string reason) =>
        new() { FileName = file, Arguments = args, Started = false, StdErr = reason, Elapsed = elapsed };

    private static void TryKill(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
