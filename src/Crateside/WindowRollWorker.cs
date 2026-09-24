using Crateside.Core.Orchestration;
using Crateside.Core.Quota;

namespace Crateside;

/// <summary>
/// Judges one track when the five-hour window has sat idle, while judging itself is paused, so
/// the windows keep rolling (#46).
/// </summary>
public sealed class WindowRollWorker(
    WindowRoll roll,
    VerdictControl judging,
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
        // A resumed stage is already judging whatever there is; a roll would only add to it.
        if (!roll.IsEnabled || !judging.IsPaused)
            return;

        var now = DateTimeOffset.UtcNow;

        // Resolved per check: a typed HttpClient held for the life of the app outlives its handler.
        var quota = services.GetRequiredService<QuotaReader>();

        if (!roll.ShouldRoll(await quota.ReadAsync(stoppingToken), now))
            return;

        var tick = await drainer.DrainOnceAsync(evenIfPaused: true, stoppingToken);

        if (tick.Outcome is not StageOutcome.Ran)
            return;

        roll.MarkRolled(now);

        logger.LogInformation(
            "Opened an idle window by judging {Subject}: {Outcome}",
            tick.Subject,
            tick.IsSuccess ? "judged" : tick.Error
        );
    }
}
