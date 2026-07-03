namespace NxCheck.Core.Model;

/// <summary>
/// triage 깊은 진단의 "사다리" 한 칸. PASS면 접히고, FAIL이면 펼쳐서
/// 어디서 끊겼는지 짚는다. (설계 5장 참조)
/// </summary>
public sealed record LadderStep(string Label, CheckStatus Status, string? Detail = null);

/// <summary>
/// 모든 체크가 반환하는 통일 결과 모델.
/// 항목 / 심각도 / 상태 / 기대값 / 실제값 / 조치힌트 + (선택)진단 사다리.
/// 출력 포맷(콘솔·JSON)은 이 위에 갈아끼운다. (설계 1장)
/// </summary>
public sealed record CheckResult
{
    /// <summary>모듈 식별자: hosts, netplan, elasticsearch, ...</summary>
    public required string Module { get; init; }

    /// <summary>체크 항목명.</summary>
    public required string Item { get; init; }

    public required Severity Severity { get; init; }

    public required CheckStatus Status { get; init; }

    /// <summary>기대값(사람이 읽는 표현).</summary>
    public string? Expected { get; init; }

    /// <summary>실제 관측값.</summary>
    public string? Actual { get; init; }

    /// <summary>조치 힌트(추정 원인·다음 행동).</summary>
    public string? Hint { get; init; }

    /// <summary>깊은 진단용 사다리 단계(없으면 비어 있음).</summary>
    public IReadOnlyList<LadderStep> Ladder { get; init; } = [];

    public bool IsOk => Status is CheckStatus.Pass or CheckStatus.Skip;

    // ── 팩토리 ─────────────────────────────────────────────

    public static CheckResult Pass(string module, string item, Severity severity,
        string? expected = null, string? actual = null) =>
        new() { Module = module, Item = item, Severity = severity, Status = CheckStatus.Pass, Expected = expected, Actual = actual };

    public static CheckResult Fail(string module, string item, Severity severity,
        string? expected = null, string? actual = null, string? hint = null,
        IReadOnlyList<LadderStep>? ladder = null) =>
        new()
        {
            Module = module, Item = item, Severity = severity, Status = CheckStatus.Fail,
            Expected = expected, Actual = actual, Hint = hint, Ladder = ladder ?? [],
        };

    public static CheckResult Skip(string module, string item, string? hint = null) =>
        new() { Module = module, Item = item, Severity = Severity.Info, Status = CheckStatus.Skip, Hint = hint };

    public static CheckResult Error(string module, string item, Severity severity = Severity.Critical,
        string? hint = null, string? actual = null) =>
        new() { Module = module, Item = item, Severity = severity, Status = CheckStatus.Error, Hint = hint, Actual = actual };
}
