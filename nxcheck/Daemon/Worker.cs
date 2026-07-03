using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using NxCheck.Core.Runners;

namespace NxCheck.Daemon;

/// <summary>
/// 상시 감시 워커. 주기적으로 전체 체크를 flow 깊이로 돌린다.
/// 데몬은 주기를 자연히 보유하므로 드랍 delta가 공짜(두 시점 비교) — 향후 flow 모듈에서 활용.
/// </summary>
public sealed class Worker(ILogger<Worker> logger, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = config.GetValue("Nxcheck:IntervalSeconds", 60);
        var expectedPath = config.GetValue("Nxcheck:ExpectedPath", "expected.yaml");
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        var engine = new CheckEngine(CheckCatalog.Default());
        var runner = new CommandRunner();

        logger.LogInformation("nxcheckd 시작 — 주기 {Interval}s, 기대값 {Path}", intervalSeconds, expectedPath);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                var expected = ExpectedSpecLoader.Load(expectedPath);
                var ctx = new CheckContext
                {
                    Mode = RunMode.Daemon,
                    Depth = CheckDepth.Flow,
                    Expected = expected,
                    Runner = runner,
                    IsInteractive = false,
                };

                var results = await engine.RunAllAsync(ctx, stoppingToken);
                Report(results);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "체크 주기 실행 중 예외");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Report(IReadOnlyList<CheckResult> results)
    {
        var exit = ExitCodePolicy.Aggregate(results);
        foreach (var r in results.Where(r => r.Status is CheckStatus.Fail or CheckStatus.Error))
        {
            var level = r.Severity == Severity.Critical ? LogLevel.Error : LogLevel.Warning;
            logger.Log(level, "[{Module}] {Item} {Status} — 기대 {Expected} / 실제 {Actual} {Hint}",
                r.Module, r.Item, r.Status, r.Expected, r.Actual, r.Hint);
        }

        logger.LogInformation("주기 완료 — 종합 종료코드 {Exit} (pass {Pass}, fail {Fail}, skip {Skip}, error {Error})",
            exit,
            results.Count(r => r.Status == CheckStatus.Pass),
            results.Count(r => r.Status == CheckStatus.Fail),
            results.Count(r => r.Status == CheckStatus.Skip),
            results.Count(r => r.Status == CheckStatus.Error));
    }
}
