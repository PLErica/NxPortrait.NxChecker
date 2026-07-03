using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.7 ufw. TODO:
/// active+enable · default deny/allow · 필요한 포트만(drift) · syslog UDP ⊆ 허용포트 교차검증
/// · /etc/ufw/before.rules ICMP unreachable/ping drop 룰. 읽기: ufw status verbose + before.rules 직독.
/// </summary>
public sealed class UfwCheck : ICheck
{
    public string Module => "ufw";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.7 참조")]);
}
