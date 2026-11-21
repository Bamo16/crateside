using Crateside.Core.Orchestration;
using Crateside.Core.SourceOffsets;
using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>
/// Measures each added source against its track's mix when it has no current measurement, and again
/// when either file changes. Never paused: it runs on the CPU and contends for nothing (ADR 0111).
/// </summary>
public sealed class SourceOffsetWorker(
    SourceOffsetPass pass,
    SourceOffsetStore store,
    SourceIndex sources,
    TracksService tracks,
    ILogger<SourceOffsetWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);

    private readonly NewWork _newWork = new(
        tracks,
        rows => Owed(rows, sources, store).Select(pair => pair.SourceKey)
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
            try
            {
                if (tracks.Current?.Rows is { } rows)
                {
                    foreach (var pair in Owed(rows, sources, store))
                    {
                        if (await pass.RunAsync([pair], stoppingToken) is { Count: > 0 })
                        {
                            logger.LogInformation(
                                "Measured {Source} against its mix",
                                Path.GetFileName(pair.SourcePath)
                            );
                            await tracks.EnsureFreshAsync(TimeSpan.Zero, stoppingToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Measuring added sources failed");
            }

            try
            {
                await _newWork.WaitAsync(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static IEnumerable<SourceOffsetPair> Owed(
        IReadOnlyList<TrackRow> rows,
        SourceIndex sources,
        SourceOffsetStore store
    ) =>
        SourceOffsetPair
            .Find(rows, sources)
            .Where(pair =>
                store.Of(pair.SourceKey) is not { } kept
                || !kept.Stands
                || kept.Mix.Path != pair.MixPath
                || kept.Source.Path != pair.SourcePath
            );
}
