using Crateside.Core.Loudness;
using Humanizer;

namespace Crateside;

/// <summary>
/// Reads every sample's loudness and keeps each saved default clip at its clip gain, once a minute.
/// Never paused: ffmpeg runs on the CPU and contends for nothing, like detection (ADR 0059).
/// </summary>
public sealed class LoudnessWorker(Levelling levelling, ILogger<LoudnessWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await levelling.ReadAsync(stoppingToken) is > 0 and var read)
                    logger.LogInformation(
                        "Read the loudness of {Count}",
                        "sample".ToQuantity(read)
                    );

                if (levelling.Level() is { Count: > 0 } levelled)
                    logger.LogInformation(
                        "Set the clip gain of {Count}: {Samples}",
                        "sample".ToQuantity(levelled.Count),
                        string.Join(", ", levelled.Take(5).Select(Path.GetFileName))
                            + (levelled.Count > 5 ? ", …" : string.Empty)
                    );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Levelling samples failed");
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
}
