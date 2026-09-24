using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Microsoft.Extensions.Options;

namespace Crateside;

/// <summary>
/// Keeps the library's keys backed by a tag-store entry, so a hand-applied tag in Ableton does
/// not reconcile them away (ADR 0023). Never paused: it contends for nothing.
/// </summary>
public sealed class BackingWorker(
    BackingRunner runner,
    IOptions<CratesidePaths> paths,
    ILogger<BackingWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(15);

    /// <summary>A stale write lost a race with Ableton, so it is worth retrying promptly.</summary>
    private static readonly TimeSpan AfterStale = TimeSpan.FromSeconds(30);

    private readonly CratesidePaths _paths = paths.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                delay = Tick();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Backing the library failed; trying again later");

                delay = AfterFailure;
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private TimeSpan Tick()
    {
        var outcome = runner.Run(_paths.MashupSamples, KeyLedger.Read(_paths.Ledger));

        switch (outcome)
        {
            case { IsSuccess: true, Applied: 0 }:
                return IdleDelay;

            case { IsSuccess: true } written:
                logger.LogInformation(
                    "Backed {Applied} of the library's keys against the tag store",
                    written.Applied
                );

                return IdleDelay;

            case { IsStale: true }:
                logger.LogInformation("The tag store moved while backing the library; retrying");

                return AfterStale;

            default:
                logger.LogWarning("Could not back the library: {Error}", outcome.Error);

                return AfterFailure;
        }
    }
}
