using NxCheck.Core.Expected;
using NxCheck.Core.Runners;

namespace NxCheck.Core.Model;

/// <summary>
/// 한 회차 체크 실행에 필요한 모든 입력을 묶는다.
/// 모드·깊이·기대값 스펙·러너·샘플링 옵션 등.
/// </summary>
public sealed class CheckContext
{
    public required RunMode Mode { get; init; }

    /// <summary>이번 실행의 깊이. triage·daemon은 Flow, --once 기본은 Static.</summary>
    public CheckDepth Depth { get; init; } = CheckDepth.Static;

    /// <summary>기대값 스펙(expected.yaml). 일부 비어 있을 수 있음.</summary>
    public required ExpectedSpec Expected { get; init; }

    /// <summary>외부 명령 실행기.</summary>
    public required ICommandRunner Runner { get; init; }

    /// <summary>one-shot 드랍 delta 샘플 윈도우(기본 10초). 데몬은 주기가 윈도우라 무관.</summary>
    public TimeSpan SampleWindow { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>능동 프로브(예: core:5141 직접 connect) 허용 여부. side-effect 있어 기본 off.</summary>
    public bool EnableActiveProbe { get; init; }

    /// <summary>TTY 부착 여부. 기대값 누락 시 대화형 입력 가능 여부 판단.</summary>
    public bool IsInteractive { get; init; }

    public bool IncludesFlow => Depth == CheckDepth.Flow;
}
