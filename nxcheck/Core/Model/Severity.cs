namespace NxCheck.Core.Model;

/// <summary>
/// 체크 항목의 심각도. 항목 자체의 중요도를 뜻하며(결과가 아님),
/// FAIL·ERROR일 때 종료코드 산정의 기준이 된다.
/// </summary>
public enum Severity
{
    /// <summary>참고용. FAIL이어도 종료코드를 올리지 않는다.</summary>
    Info,

    /// <summary>경고. FAIL·ERROR 시 종료코드 1.</summary>
    Warning,

    /// <summary>치명적. FAIL·ERROR 시 종료코드 2.</summary>
    Critical,
}
