using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Library;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;

namespace Crateside;

/// <summary>Scans the queue and composes every row the Tracks page shows from what is on disk.</summary>
public sealed class TracksReader(
    WorkListService workLists,
    StagingReader staging,
    CratesidePaths paths,
    JobStore jobs,
    ResolvedNameStore names,
    WorkingSet workingSet,
    PresetCatalogReader presets,
    FileIndexReader fileIndex,
    DeletionLog deletions,
    GenreStore genres,
    FamiliarityStore familiarity,
    WarpTempoReader warpTempos,
    BeatReader beats,
    WrittenWarps written,
    TrackMarkStore marks,
    SourceIndex sources,
    ILogger<TracksReader> logger
) : ITracksReader
{
    public async Task<TracksView> ReadAsync(CancellationToken ct = default)
    {
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
                names.Read(),
                workingSet.Acquisitions(),
                await presets.ReadAsync(ct),
                AbletonReads.TagsOrEmpty(fileIndex.Read, paths.LibraryFolder),
                deletions.All(),
                genres.Read(),
                familiarity.Read(),
                warpTempos.Read,
                beats.Read,
                written.Of,
                marks.Read()
            );

            warpTempos.Save();
            sources.Refresh(scan.Processed, scan.Ledger);

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
