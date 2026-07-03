using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.6 nxcollector (자체 서비스). TODO:
/// active+enabled · 설정 파싱 · 소유권/퍼미션 · 크래시루프(daemon/triage: NRestarts)
/// · core 연결(ESTAB :5141, any-of) ← QA 핵심. 버전 비교는 미사용.
/// </summary>
public sealed class NxCollectorCheck : ICheck
{
    public string Module => "nxcollector";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.6 참조")]);
}
