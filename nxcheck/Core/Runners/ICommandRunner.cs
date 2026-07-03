namespace NxCheck.Core.Runners;

/// <summary>
/// 외부 리눅스 도구 호출 추상화. 모든 호출에 타임아웃과 로케일 고정이 적용된다.
/// 테스트에서는 가짜 구현으로 교체.
/// </summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
