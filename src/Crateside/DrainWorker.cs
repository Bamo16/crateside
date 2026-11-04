using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Separation;

namespace Crateside;

/// <summary>
/// The loop that keeps the queue draining. Deliberately thin — the decisions belong to
/// <see cref="SeparationDrainer"/> and <see cref="SeparationSelector"/>.
/// </summary>
public sealed class DrainWorker(
    SeparationDrainer drainer,
    DrainControl control,
    StageSignals signals,
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
            catch (Exception ex)
            {
                logger.LogError(ex, "Drain tick failed");
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
    /// Squares the job store against reality before anything launches, since a previous run's
    /// <c>stemforge-cli</c> may still be alive, or may have finished while crateside was down.
    /// </summary>
    private void Reconcile()
    {
        if (JobObject.KillsChildrenOnExit())
            logger.LogWarning(
                "crateside is running inside a job that kills its children when it exits, as "
                    + "under dotnet run: a restart will end any separation in progress. Start it "
                    + "with the run task."
            );

        var reconciliation = drainer.Reconcile();

        foreach (var job in reconciliation.Adopted)
            logger.LogInformation(
                "Adopted separation of {TrackKey} still running as process {ProcessId}",
                job.TrackKey,
                job.ProcessId
            );

        LogSettled(reconciliation);
    }

    private void LogSettled(SeparationReconciliation reconciliation)
    {
        foreach (var settled in reconciliation.Finished)
            logger.LogInformation(
                "Separation of {TrackKey} finished while nobody was watching ({Count} stems)",
                settled.Job.TrackKey,
                settled.OutputFiles.Count
            );

        foreach (var job in reconciliation.Abandoned)
            logger.LogWarning(
                "Separation of {TrackKey} did not finish: {Error}",
                job.TrackKey,
                job.Error
            );

        if (reconciliation.Finished.Count > 0)
            signals.WakeAllBut(JobStage.Separation);
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        // An adopted separation reports nothing when it ends; this is how it gets noticed.
        LogSettled(drainer.SettleEnded());

        var tick = await drainer.DrainOnceAsync(stoppingToken);

        switch (tick)
        {
            case { Outcome: DrainOutcome.Ran, Order: { } order, Result: { } result }:
                LogRun(order, result);

                // Stems nobody has been told about yet: the handoff would otherwise wait out its
                // own interval behind a separation that has already finished.
                if (result.Outcome is SeparationOutcome.Succeeded)
                    signals.WakeAllBut(JobStage.Separation);

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
