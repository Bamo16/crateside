using Crateside.Core.Orchestration;

namespace Crateside;

/// <summary>
/// The loop that keeps the queue draining. Deliberately thin — every decision it appears to make
/// belongs to <see cref="SeparationDrainer"/> or <see cref="SeparationSelector"/>, which are
/// testable without a host.
/// </summary>
public sealed class DrainWorker(
    SeparationDrainer drainer,
    SeparationInterlock interlock,
    ILogger<DrainWorker> logger
) : BackgroundService
{
    // Long enough that an idle queue is not re-read constantly, short enough that a track added
    // from a phone starts within a couple of minutes.
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(2);

    // A separation takes ~6-7 minutes, so back-to-back ticks need no pause of their own.
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
                // One bad tick must not end draining for the lifetime of the app. The job store
                // already recorded whatever happened, so the next pass sees it as a strike.
                logger.LogError(e, "Drain tick failed");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Squares the job store against reality before anything is allowed to launch. A
    /// <c>stemforge-cli</c> started by a previous crateside is still running — on Windows a
    /// child is not killed with its parent — and must be found rather than duplicated.
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

                // Refused means the GPU is busy with an adopted run, so wait rather than spin.
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
