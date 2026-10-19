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
    private const int ListedAtMost = 5;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (levelling.Unread() is { Count: > 0 } unread)
                {
                    logger.LogInformation(
                        "Reading the loudness of {Count}",
                        "sample".ToQuantity(unread.Count)
                    );
                    await levelling.ReadAsync(unread, stoppingToken);
                    logger.LogInformation(
                        "Read the loudness of {Count}",
                        "sample".ToQuantity(unread.Count)
                    );
                }

                if (levelling.Level() is { Count: > 0 } levelled)
                    logger.LogInformation(
                        "Set the clip gain of {Count}: {Samples}",
                        "sample".ToQuantity(levelled.Count),
                        Listed(levelled)
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

    private static string Listed(IReadOnlyList<string> samples) =>
        samples.Count > ListedAtMost
            ? $"{string.Join(", ", samples.Take(ListedAtMost).Select(Path.GetFileName))} and {samples.Count - ListedAtMost} more"
            : samples.Select(Path.GetFileName).Humanize();
}
