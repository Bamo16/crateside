using Crateside.Core;
using Crateside.Core.Beats;
using Humanizer;

namespace Crateside;

/// <summary>
/// Reads <c>beatdetect</c> on every instrumental without a current reading, staging before the
/// library. Never paused: it runs on the CPU and contends for nothing, like detection (#75, #76).
/// </summary>
public sealed class BeatWorker(BeatReader reader, CratesidePaths paths, ILogger<BeatWorker> logger)
    : BackgroundService
{
    private const int AtOnce = 3;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);

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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reading beatdetect failed; trying again later");

                delay = AfterFailure;
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        if (await reader.RefreshBuildAsync(stoppingToken) is null)
        {
            logger.LogWarning("ffmpeg on PATH did not say which build it is; beatdetect waits");

            return AfterFailure;
        }

        string[] unread =
        [
            .. BeatReader.Instrumentals(paths).Where(file => reader.Read(file) is null),
        ];

        if (unread.Length is 0)
            return IdleDelay;

        logger.LogInformation(
            "Reading beatdetect on {Count}",
            "instrumental".ToQuantity(unread.Length)
        );

        await Parallel.ForEachAsync(
            unread,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = AtOnce,
                CancellationToken = stoppingToken,
            },
            async (file, ct) =>
            {
                await reader.MeasureAsync(file, ct);
                reader.Save();
            }
        );

        logger.LogInformation(
            "Read beatdetect on {Count}",
            "instrumental".ToQuantity(unread.Length)
        );

        return IdleDelay;
    }
}
