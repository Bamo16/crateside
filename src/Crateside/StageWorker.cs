using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;

namespace Crateside;

/// <summary>
/// The loop that keeps one stage draining. Deliberately thin — the decisions belong to a drainer
/// or a selector.
/// </summary>
public sealed class StageWorker(
    IStageDrainer drainer,
    JobStore jobs,
    ILogger<StageWorker> logger,
    StageSignals signals
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Reopen();

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                delay = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Stage} tick failed", drainer.Stage);
            }

            try
            {
                await signals.For(drainer.Stage).WaitForWorkAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Squares the job store against reality before anything starts, or a job an interrupted run
    /// left <c>Running</c> is skipped for ever.
    /// </summary>
    private void Reopen()
    {
        foreach (var job in jobs.Open(drainer.Stage))
        {
            jobs.Record(
                job.VideoId,
                drainer.Stage,
                JobStatus.Cancelled,
                error: "did not survive a restart"
            );

            logger.LogInformation(
                "{Stage} for {VideoId} did not survive a restart; it goes back in line",
                drainer.Stage,
                job.VideoId
            );
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        var tick = await drainer.DrainOnceAsync(stoppingToken);

        switch (tick)
        {
            case { Outcome: StageOutcome.Ran, Subject: { } subject }:
                if (tick.IsSuccess)
                {
                    logger.LogInformation(
                        "{Stage}: {Title}{Note}",
                        drainer.Stage,
                        subject,
                        tick.Note is { } note ? $" — {note}" : string.Empty
                    );

                    // A failure made nothing for anyone downstream, so only success broadcasts.
                    signals.WakeAllBut(drainer.Stage);
                }
                else
                    logger.LogWarning(
                        "{Stage} failed for {Title}: {Error}",
                        drainer.Stage,
                        subject,
                        tick.Error
                    );

                return BusyDelay;

            case { Outcome: StageOutcome.Unavailable, Error: var error }:
                logger.LogWarning("Queue could not be read: {Error}", error);

                return IdleDelay;

            default:
                return IdleDelay;
        }
    }
}
