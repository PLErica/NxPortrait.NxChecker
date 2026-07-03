using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.2 netplan (read-only). TODO:
/// static — /etc/netplan/*.yaml 파싱 · 실제 IP(ip -j addr) 대조 · bonding(/proc/net/bonding) 검증
/// flow   — 기본 게이트웨이 ping 도달
/// </summary>
public sealed class NetplanCheck : ICheck
{
    public string Module => "netplan";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.2 참조")]);
}
