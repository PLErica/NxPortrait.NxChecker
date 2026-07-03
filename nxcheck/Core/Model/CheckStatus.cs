namespace NxCheck.Core.Model;

/// <summary>체크 1건의 판정 상태.</summary>
public enum CheckStatus
{
    /// <summary>기대와 실제가 일치.</summary>
    Pass,

    /// <summary>기대와 실제가 불일치.</summary>
    Fail,

    /// <summary>판정 조건 미충족으로 건너뜀(기대값 미정의·트래픽 부족 등). 종료코드 무영향.</summary>
    Skip,

    /// <summary>체크 자체가 못 돌았음(도구 부재·타임아웃·파싱 실패 등).</summary>
    Error,
}
