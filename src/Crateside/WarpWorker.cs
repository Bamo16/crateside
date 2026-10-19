using System.Diagnostics;
using Crateside.Core.Ableton;
using Crateside.Core.Staging;
using Crateside.Core.Warps;
using Humanizer;
using Microsoft.Extensions.Options;

namespace Crateside;

/// <summary>
/// Lends staged stems their siblings' finished warps, or writes starter warps, and marks each stem's
/// state in Live (#130), once a minute.
/// Decoding runs on the CPU and contends for nothing, so this runs unattended like detection
/// (ADR 0043, ADR 0047).
/// </summary>
public sealed class WarpWorker(
    WarpLending lending,
    WarpActions actions,
    StagingMarks marks,
    IOptions<WarpOptions> options,
    ILogger<WarpWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lends =
            options.Value is not { LendAutomatically: false, StarterWarps: StarterWarps.Off };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var putBack in actions.PutBack())
                    logger.LogInformation(
                        "Put the warp back into Live's fresh analysis of {Target}",
                        Path.GetFileName(putBack)
                    );

                if (lends)
                {
                    foreach (var loan in await lending.LendAsync(stoppingToken))
                        Log(loan);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lending warps failed");
            }

            try
            {
                Mark();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Marking staged stems in Live failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Mark()
    {
        foreach (var recoloured in marks.Recolour())
            logger.LogInformation("Gave {Target} its clip colour", Path.GetFileName(recoloured));

        switch (marks.Collect())
        {
            case TagWriteOutcome.Written(0):
                break;

            case TagWriteOutcome.Written(var moved):
                logger.LogInformation(
                    "Moved {Moved} between Live's Collections",
                    "staged file".ToQuantity(moved)
                );

                break;

            // Another write got to the store first; the next minute tries again.
            case TagWriteOutcome.Stale:
                break;

            case TagWriteOutcome.Rejected(var error):
                logger.LogWarning("Could not put staged stems in Collections: {Error}", error);

                break;

            default:
                throw new UnreachableException();
        }
    }

    private void Log(WarpLoan loan)
    {
        var target = Path.GetFileName(loan.Target);

        switch (loan.Outcome)
        {
            case WarpOutcome.Written(var from, var transientsFrom):
                logger.LogInformation(
                    "Warped {Target} with the warp of {From}, transients {Transients}",
                    target,
                    Path.GetFileName(from),
                    Transients(transientsFrom)
                );

                break;

            case WarpOutcome.Started(var bpm, var transientsFrom):
                logger.LogInformation(
                    "Gave {Target} a starter warp at {Bpm} BPM, transients {Transients}",
                    target,
                    bpm,
                    Transients(transientsFrom)
                );

                break;

            case WarpOutcome.Unwarped(var transientsFrom):
                logger.LogInformation(
                    "Gave {Target} a clip with Warp off at its gain, transients {Transients}",
                    target,
                    Transients(transientsFrom)
                );

                break;

            case WarpOutcome.SiblingsDisagree(var siblings):
                logger.LogWarning(
                    "Not warping {Target}: the stems it could take a warp from hold different ones ({Siblings})",
                    target,
                    string.Join(", ", siblings.Select(Path.GetFileName))
                );

                break;

            case WarpOutcome.LengthsDiffer(var sibling, var siblingFrames, var targetFrames):
                logger.LogWarning(
                    "Not warping {Target}: {TargetFrames} frames long, but {Sibling} is {SiblingFrames}",
                    target,
                    targetFrames,
                    Path.GetFileName(sibling),
                    siblingFrames
                );

                break;

            case WarpOutcome.Failed(var error):
                logger.LogWarning("Could not warp {Target}: {Error}", target, error);

                break;
        }
    }

    private static string Transients(string? from) =>
        from is null
            ? "detected by crateside"
            : $"from Live's analysis of {Path.GetFileName(from)}";
}
