using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.5 nginx. TODO:
/// nginx -t · active+enabled · 80/443 listen · TLS 인증서 존재/만료/체인
/// · sites-enabled dangling 링크 없음 · default 사이트 없음 · stream 분배(→4.8).
/// </summary>
public sealed class NginxCheck : ICheck
{
    public string Module => "nginx";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.5 참조")]);
}
