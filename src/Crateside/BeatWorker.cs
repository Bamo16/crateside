using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Tracks;
using Humanizer;

namespace Crateside;

/// <summary>
/// Reads <c>beatdetect</c> on each upload's reference file, its source else its instrumental, when it
/// has no current reading, then again on those read by an older build than the pin, staging before
/// the library. Never paused: it runs on the CPU and contends for nothing, like detection (#75, #76, ADR 0065).
/// </summary>
public sealed class BeatWorker(
    BeatReader reader,
    CratesidePaths paths,
    FolderInfoReader folderInfo,
    TracksService tracks,
    ILogger<BeatWorker> logger
) : BackgroundService
{
    private const int AtOnce = 3;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);

    private readonly NewWork _newWork = new(
        tracks,
        rows =>
            rows.Where(row =>
                    row.Samples.Any(sample =>
                        sample is { IsInLibrary: false, Beats: null }
                        && sample.Label.StartsWith(
                            "Instrumental",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                .Select(row => row.TrackKey)
    );

    public override void Dispose()
    {
        _newWork.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
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

            await _newWork.WaitAsync(delay, stoppingToken);
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        if (await reader.RefreshBuildAsync(stoppingToken) is null)
        {
            logger.LogWarning(
                "{Ffmpeg} did not say which build it is; beatdetect waits",
                paths.BeatdetectFfmpeg
            );

            return AfterFailure;
        }

        string[] toRead =
        [
            .. reader.ToRead([.. reader.ReferencesOf(BeatReader.Instrumentals(paths, folderInfo))]),
        ];

        reader.Begin(toRead.Length);

        if (toRead.Length is 0)
            return IdleDelay;

        logger.LogInformation("Reading beatdetect on {Count}", "file".ToQuantity(toRead.Length));

        await Parallel.ForEachAsync(
            toRead,
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

        logger.LogInformation("Read beatdetect on {Count}", "file".ToQuantity(toRead.Length));

        return IdleDelay;
    }
}
