using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 등록된 모든 체크를 순서대로 돌리고 결과를 모은다.
/// 한 모듈이 예외로 죽어도 ERROR 결과로 흡수하고 나머지를 계속 돈다
/// (triage는 뭔가 고장난 순간이므로 한 곳이 터져도 전체를 멈추지 않는다).
/// </summary>
public sealed class CheckEngine(IEnumerable<ICheck> checks)
{
    private readonly IReadOnlyList<ICheck> _checks = checks.ToList();

    public async Task<IReadOnlyList<CheckResult>> RunAllAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var all = new List<CheckResult>();
        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                all.AddRange(await check.RunAsync(ctx, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                all.Add(CheckResult.Error(check.Module, "모듈 실행", Severity.Critical,
                    hint: $"예기치 못한 예외: {ex.Message}"));
            }
        }
        return all;
    }
}
