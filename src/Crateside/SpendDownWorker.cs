using Crateside.Core.Quota;

namespace Crateside;

/// <summary>
/// Spends the week down on the backlog before the weekly reset, logging weekly use around each
/// track (ADR 0063).
/// </summary>
public sealed class SpendDownWorker(
    SpendDown spend,
    TimeProvider time,
    ILogger<SpendDownWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SpendDownPass? pass = null;

            try
            {
                pass = await spend.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Spend-down check failed; trying again later");
            }

            if (pass is not null)
                Log(pass);

            // A failure goes round the slow way, so an agent that cannot start is not retried at once.
            if (pass is not { Tick.IsSuccess: true })
                await Task.Delay(CheckEvery, time, stoppingToken);
        }
    }

    private void Log(SpendDownPass pass) =>
        logger.LogInformation(
            "Spend-down judged {Subject}: {Outcome}; weekly {WeeklyBefore:0.#}% before, {WeeklyAfter}% after, delta {WeeklyDelta}",
            pass.Tick.Subject,
            pass.Tick.IsSuccess ? "judged" : pass.Tick.Error,
            pass.WeeklyBefore,
            pass.WeeklyAfter is { } after ? $"{after:0.#}" : "unread",
            pass.Delta is { } delta ? $"{delta:+0.#;-0.#;0}" : "unknown"
        );
}
