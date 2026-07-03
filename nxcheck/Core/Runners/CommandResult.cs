namespace NxCheck.Core.Runners;

/// <summary>외부 명령 1회 실행 결과.</summary>
public sealed record CommandResult
{
    public required string FileName { get; init; }
    public required string Arguments { get; init; }

    /// <summary>프로세스가 실제로 시작됐는지(도구 부재 시 false).</summary>
    public bool Started { get; init; }

    /// <summary>타임아웃으로 강제 종료됐는지.</summary>
    public bool TimedOut { get; init; }

    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public TimeSpan Elapsed { get; init; }

    /// <summary>정상 시작 + 타임아웃 없음 + exit 0.</summary>
    public bool Success => Started && !TimedOut && ExitCode == 0;

    /// <summary>실패 사유 요약(없으면 null).</summary>
    public string? FailureReason =>
        !Started ? "명령을 시작할 수 없음(도구 부재 가능)"
        : TimedOut ? $"타임아웃({Elapsed.TotalSeconds:0.#}s)"
        : ExitCode != 0 ? $"exit {ExitCode}"
        : null;
}
