using Crateside.Core.Orchestration;

namespace Crateside;

/// <summary>
/// The loop that keeps one stage draining. Deliberately thin — every decision it appears to make
/// belongs to a drainer or a selector, which are testable without a host.
/// </summary>
/// <remarks>
/// <para>
/// One of these per stage, rather than one loop walking a track through all three. Head-of-line
/// blocking is the reason: a single sequential loop would mean a paused GPU stopped detection of
/// everything queued behind it, and a stage failing on one track would stall other tracks in
/// other stages.
/// </para>
/// <para>
/// <strong>Separation is not driven by this.</strong> <see cref="DrainWorker"/> keeps its own
/// loop, because it has to reconcile orphaned processes on startup and honour a pause — neither
/// of which the stages that contend for nothing have any use for.
/// </para>
/// </remarks>
/// <param name="control">
/// Supplied only for a stage that can be stopped, so that pressing resume starts work now
/// rather than whenever the idle delay happens to run out. A stage with nothing to stop for —
/// evidence — passes none, and simply waits out the clock.
/// </param>
public sealed class StageWorker(
    IStageDrainer drainer,
    ILogger<StageWorker> logger,
    DrainControl? control = null
) : BackgroundService
{
    // Long enough that an idle queue is not re-read constantly, short enough that a track added
    // from a phone starts within a couple of minutes.
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(2);

    // Work here takes tens of seconds at least, so back-to-back ticks need no pause of their own.
    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                // One bad tick must not end this stage for the lifetime of the app. The job store
                // already recorded whatever happened, so the next pass sees it as a strike.
                logger.LogError(e, "{Stage} tick failed", drainer.Stage);
            }

            try
            {
                // Waiting on the control rather than the clock where there is one, so pressing
                // resume starts work now instead of up to two minutes later, which reads as a
                // broken button rather than a slow one.
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

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        var tick = await drainer.DrainOnceAsync(stoppingToken);

        switch (tick)
        {
            case { Outcome: StageOutcome.Ran, Order: { } order }:
                if (tick.IsSuccess)
                    logger.LogInformation("{Stage}: {Title}", drainer.Stage, order.Title);
                else
                    logger.LogWarning(
                        "{Stage} failed for {Title}: {Error}",
                        drainer.Stage,
                        order.Title,
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
