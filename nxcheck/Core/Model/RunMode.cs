namespace NxCheck.Core.Model;

/// <summary>진입 모드 (설계 2장). 세 모드 모두 read-only, 동일 체크 라이브러리 호출.</summary>
public enum RunMode
{
    /// <summary>수동/파이프라인 1회. 출고 직전 QA.</summary>
    Once,

    /// <summary>systemd 상시. 현장 감시, flow delta 자연 보유.</summary>
    Daemon,

    /// <summary>문제 발생 시 수동 1회. 깊은 진단 사다리.</summary>
    Triage,
}

/// <summary>체크 깊이 (설계 3장).</summary>
public enum CheckDepth
{
    /// <summary>빠른 판정. 흐름은 보지 않음(출고 전).</summary>
    Static,

    /// <summary>깊은 진단. 런타임 흐름·드랍률까지(출고 후).</summary>
    Flow,
}
