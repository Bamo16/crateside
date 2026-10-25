using Crateside.Core.Loudness;
using Crateside.Core.Watching;
using Humanizer;

namespace Crateside;

/// <summary>
/// Reads every sample's loudness and keeps each saved default clip at its clip gain, when Staging or the library changes and every ten minutes besides.
/// Never paused: ffmpeg runs on the CPU and contends for nothing, like detection (ADR 0059).
/// </summary>
public sealed class LoudnessWorker(
    Leveling leveling,
    FileChanges changes,
    ILogger<LoudnessWorker> logger
) : BackgroundService
{
    private const int ListedAtMost = 5;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly FileChanges.Listener _wake = changes.Listen();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (leveling.Unread() is { Count: > 0 } unread)
                {
                    logger.LogInformation(
                        "Reading the loudness of {Count}",
                        "sample".ToQuantity(unread.Count)
                    );
                    await leveling.ReadAsync(unread, stoppingToken);
                    logger.LogInformation(
                        "Read the loudness of {Count}",
                        "sample".ToQuantity(unread.Count)
                    );
                }

                if (leveling.Level() is { Count: > 0 } leveled)
                    logger.LogInformation(
                        "Set the clip gain of {Count}: {Samples}",
                        "sample".ToQuantity(leveled.Count),
                        Listed(leveled)
                    );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Leveling samples failed");
            }

            try
            {
                await _wake.WaitAsync(Interval, stoppingToken);
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
