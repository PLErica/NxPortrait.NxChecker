namespace NxCheck.Core.Model;

/// <summary>
/// 종료코드 정책 (설계 1장 확정):
/// 0=전부 통과, 1=경고, 2=치명적.
/// FAIL·ERROR는 항목 심각도로 매핑 — critical→2, warning→1, info→0.
/// (critical 체크가 못 돌아도(ERROR) 2로 나가, "못 돌았는데 0"을 막는다.)
/// PASS·SKIP은 종료코드에 영향 없음.
/// </summary>
public static class ExitCodePolicy
{
    public const int AllPass = 0;
    public const int Warning = 1;
    public const int Critical = 2;

    public static int ForResult(CheckResult r) => r.Status switch
    {
        CheckStatus.Pass or CheckStatus.Skip => AllPass,
        CheckStatus.Fail or CheckStatus.Error => r.Severity switch
        {
            Severity.Critical => Critical,
            Severity.Warning => Warning,
            _ => AllPass, // Info
        },
        _ => AllPass,
    };

    /// <summary>전체 결과에서 가장 높은 종료코드를 취한다.</summary>
    public static int Aggregate(IEnumerable<CheckResult> results) =>
        results.Select(ForResult).DefaultIfEmpty(AllPass).Max();
}
