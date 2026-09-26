using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Quota;

namespace Crateside;

/// <summary>
/// Judges one library backfill track when the five-hour window has sat idle, whether or not
/// judging is paused (ADR 0035).
/// </summary>
public sealed class WindowRollWorker(
    WindowRoll roll,
    PauseStore remembered,
    VerdictDrainer drainer,
    IServiceProvider services,
    ILogger<WindowRollWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Window roll check failed; trying again later");
            }

            await Task.Delay(CheckEvery, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        if (!roll.IsEnabled)
            return;

        var now = DateTimeOffset.UtcNow;

        // Resolved per check: a typed HttpClient held for the life of the app outlives its handler.
        var quota = services.GetRequiredService<QuotaReader>();

        if (!roll.ShouldRoll(await quota.ReadAsync(stoppingToken), now))
            return;

        var tick = await drainer.JudgeBackfillAsync(stoppingToken);

        if (tick.Outcome is not StageOutcome.Ran)
            return;

        roll.MarkRolled(now);
        remembered.RememberBackfillJudged(now);

        logger.LogInformation(
            "Opened an idle window by judging {Subject}: {Outcome}",
            tick.Subject,
            tick.IsSuccess ? "judged" : tick.Error
        );
    }
}
