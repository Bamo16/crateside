using Crateside.Core.Orchestration;

namespace Crateside;

/// <summary>
/// The loop that keeps one stage draining. Deliberately thin — the decisions belong to a drainer
/// or a selector.
/// </summary>
/// <param name="control">
/// Only stages that can be paused supply this. Null means the stage always waits out the idle
/// delay instead of waking on resume.
/// </param>
public sealed class StageWorker(
    IStageDrainer drainer,
    JobStore jobs,
    ILogger<StageWorker> logger,
    DrainControl? control = null
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
            catch (Exception e)
            {
                logger.LogError(e, "{Stage} tick failed", drainer.Stage);
            }

            try
            {
                if (control is not null)
                    await control.WaitForWorkAsync(delay, stoppingToken);
                else
                    await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Squares the job store against reality before anything is allowed to start.
    /// </summary>
    /// <remarks>
    /// A job left <c>Running</c> by an interrupted restart looks like work still in progress to
    /// every selector, so without this the track would be skipped forever. It is recorded as
    /// <see cref="JobStatus.Cancelled"/> rather than failed, since a restart is not the track's
    /// fault.
    /// </remarks>
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
                    logger.LogInformation(
                        "{Stage}: {Title}{Note}",
                        drainer.Stage,
                        subject,
                        tick.Note is { } note ? $" — {note}" : string.Empty
                    );
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
