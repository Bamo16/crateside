using Crateside.Core.Orchestration;

namespace Crateside;

/// <summary>
/// The loop that keeps the queue draining. Deliberately thin — the decisions belong to
/// <see cref="SeparationDrainer"/> and <see cref="SeparationSelector"/>.
/// </summary>
public sealed class DrainWorker(
    SeparationDrainer drainer,
    SeparationInterlock interlock,
    DrainControl control,
    ILogger<DrainWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Reconcile();

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
                logger.LogError(e, "Drain tick failed");
            }

            try
            {
                await control.WaitForWorkAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Squares the job store against reality before anything is allowed to launch. On Windows a
    /// child process is not killed with its parent, so a <c>stemforge-cli</c> from a previous
    /// crateside run may still be alive and must be found rather than duplicated.
    /// </summary>
    private void Reconcile()
    {
        var reconciliation = interlock.Reconcile();

        foreach (var job in reconciliation.Adopted)
            logger.LogInformation(
                "Adopted separation of {VideoId} still running as process {ProcessId}",
                job.VideoId,
                job.ProcessId
            );

        foreach (var job in reconciliation.Abandoned)
            logger.LogWarning(
                "Separation of {VideoId} did not survive: {Error}",
                job.VideoId,
                job.Error
            );
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        var tick = await drainer.DrainOnceAsync(stoppingToken);

        switch (tick)
        {
            case { Outcome: DrainOutcome.Ran, Order: { } order, Result: { } result }:
                LogRun(order, result);

                // Refused: the GPU is busy with an adopted run.
                return result.Outcome is SeparationOutcome.Refused ? IdleDelay : BusyDelay;

            case { Outcome: DrainOutcome.Unavailable, Error: var error }:
                logger.LogWarning("Queue could not be read: {Error}", error);

                return IdleDelay;

            default:
                return IdleDelay;
        }
    }

    private void LogRun(WorkOrder order, SeparationResult result)
    {
        if (result.Outcome is SeparationOutcome.Succeeded)
            logger.LogInformation(
                "Separated {Title} into {Destination} ({Count} stems)",
                order.Title,
                order.Destination,
                result.OutputFiles.Count
            );
        else
            logger.LogWarning(
                "Separation of {Title} {Outcome}: {Error}",
                order.Title,
                result.Outcome,
                result.Error
            );
    }
}
