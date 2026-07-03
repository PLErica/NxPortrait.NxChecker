using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.8 nginx ↔ syslog-ng 포트 정합(contract). TODO:
/// 집합 비교(갯수 아님, v4/v6 정규화): nginx -T stream upstream 포트 == ss -ulnp UDP 5140~5144.
/// 선행 의존: nginx -T 실패 시 ERROR/SKIP. flow에서 포트별 드랍률(2단 20/30%, delta, 최소표본 가드).
/// </summary>
public sealed class CrossCheck : ICheck
{
    public string Module => "crosscheck";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.8 참조")]);
}
