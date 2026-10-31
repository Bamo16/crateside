using System.Diagnostics;
using Crateside.Core;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside;

/// <summary>Scans the queue and composes every row the Tracks page shows from what is on disk.</summary>
public sealed class TracksReader(
    WorkListService workLists,
    StagingReader staging,
    CratesidePaths paths,
    JobStore jobs,
    TrackAnnotationReader annotations,
    SampleReadingsReader samples,
    ILogger<TracksReader> logger
) : ITracksReader
{
    public async Task<TracksView> ReadAsync(CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var scan = await workLists.ScanAsync(ct);

            var view = TracksView.Compose(
                scan.View,
                scan.Ledger,
                staging.Read(paths),
                scan.Processed.StemsByVideoId.Concat(SourcelessSongs.Scan(paths.LibraryFolder))
                    .ToDictionary(),
                paths.LibraryFolder,
                jobs,
                scan.Instructions,
                annotations.Read(scan.Ledger),
                await samples.ReadAsync(ct)
            );

            if (Stopwatch.GetElapsedTime(started) is { TotalSeconds: >= 1 } took)
                logger.LogInformation(
                    "Reading the Tracks rows took {Elapsed} ms",
                    (long)took.TotalMilliseconds
                );

            return view with
            {
                ReadAt = scan.ReadAt,
                PlaylistProblem = scan.PlaylistProblem,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reading the queue failed");

            throw;
        }
    }
}
