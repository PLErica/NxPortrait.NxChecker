using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.4 syslog-ng (다중 인스턴스 — 포트당 1개, 5140~5144 가변). TODO:
/// 인스턴스별 active+enabled · syslog-ng -s 문법 · UDP 5140~5144 바인드(ss -ulnp)
/// · destination · rsyslog 비활성. 포트 정합은 4.8 crosscheck.
/// </summary>
public sealed class SyslogNgCheck : ICheck
{
    public string Module => "syslog-ng";

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(
            [CheckResult.Skip(Module, "미구현", hint: "설계 4.4 참조")]);
}
