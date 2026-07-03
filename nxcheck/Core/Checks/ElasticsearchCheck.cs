using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.3 elasticsearch. TODO:
/// active+enabled · expected URL(https, 비표준 포트) listen · _cat/health?v 파싱(red FAIL/yellow 주의)
/// · node.total 일치 · heap config(Xms=Xmx,&lt;31GB) · 디스크 watermark. (RAM 사용률은 미검사)
/// </summary>
public sealed class ElasticsearchCheck : ICheck
{
    public string Module => "elasticsearch";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.3 참조")]);
}
