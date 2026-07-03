using NxCheck.Core.Runners;

namespace NxCheck.Core.Checks.Support;

/// <summary>
/// systemd 유닛 상태 조회 헬퍼. 여러 모듈(nxcollector·nginx·syslog-ng·elasticsearch)이 공유.
/// is-active / is-enabled는 비활성 시 exit≠0이라도 stdout으로 상태를 뱉으므로,
/// Success가 아니라 Started + stdout으로 판정한다.
/// </summary>
public sealed class SystemdProbe(ICommandRunner runner)
{
    public async Task<ServiceState> IsActiveAsync(string unit, CancellationToken ct = default)
    {
        var r = await runner.RunAsync("systemctl", ["is-active", unit], ct: ct);
        var raw = r.StdOut.Trim();
        return new ServiceState(r.Started, raw == "active", raw, r);
    }

    public async Task<ServiceState> IsEnabledAsync(string unit, CancellationToken ct = default)
    {
        var r = await runner.RunAsync("systemctl", ["is-enabled", unit], ct: ct);
        var raw = r.StdOut.Trim();
        // enabled / enabled-runtime 를 "부팅 시 자동 시작"으로 인정.
        return new ServiceState(r.Started, raw.StartsWith("enabled", StringComparison.Ordinal), raw, r);
    }

    /// <summary>재시작 횟수. 조회 실패면 null.</summary>
    public async Task<int?> NRestartsAsync(string unit, CancellationToken ct = default)
    {
        var r = await runner.RunAsync("systemctl", ["show", unit, "-p", "NRestarts", "--value"], ct: ct);
        return r.Success && int.TryParse(r.StdOut.Trim(), out var n) ? n : null;
    }
}

/// <summary>
/// 서비스 상태 조회 결과.
/// </summary>
/// <param name="Queried">systemctl 자체가 실행됐는지(도구 부재 시 false).</param>
/// <param name="Ok">기대 상태(active/enabled)인지.</param>
/// <param name="Raw">stdout 원문(active/inactive/failed/enabled/disabled ...).</param>
/// <param name="Command">원 명령 결과.</param>
public readonly record struct ServiceState(bool Queried, bool Ok, string Raw, CommandResult Command);
