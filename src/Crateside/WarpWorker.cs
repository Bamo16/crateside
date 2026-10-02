using Crateside.Core.Warps;
using Microsoft.Extensions.Options;

namespace Crateside;

/// <summary>
/// Lends staged stems their siblings' finished warps once a minute. Decoding runs on the CPU and
/// contends for nothing, so this runs unattended like detection (ADR 0043).
/// </summary>
public sealed class WarpWorker(
    WarpLending lending,
    IOptions<WarpOptions> options,
    ILogger<WarpWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.LendAutomatically)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var loan in await lending.LendAsync(stoppingToken))
                    Log(loan);
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
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Log(WarpLoan loan)
    {
        var target = Path.GetFileName(loan.Target);

        switch (loan.Outcome)
        {
            case WarpBorrowOutcome.Written(var from, var transientsFrom):
                logger.LogInformation(
                    "Warped {Target} with the warp of {From}, transients {Transients}",
                    target,
                    Path.GetFileName(from),
                    transientsFrom is null
                        ? "detected by crateside"
                        : $"from Live's analysis of {Path.GetFileName(transientsFrom)}"
                );

                break;

            case WarpBorrowOutcome.SiblingsDisagree(var siblings):
                logger.LogWarning(
                    "Not warping {Target}: its instrumentals hold different warps ({Siblings})",
                    target,
                    string.Join(", ", siblings.Select(Path.GetFileName))
                );

                break;

            case WarpBorrowOutcome.LengthsDiffer(var sibling, var siblingFrames, var targetFrames):
                logger.LogWarning(
                    "Not warping {Target}: {TargetFrames} frames long, but {Sibling} is {SiblingFrames}",
                    target,
                    targetFrames,
                    Path.GetFileName(sibling),
                    siblingFrames
                );

                break;

            case WarpBorrowOutcome.Failed(var error):
                logger.LogWarning("Could not warp {Target}: {Error}", target, error);

                break;
        }
    }
}
