using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Samples;
using Crateside.Core.Warps;

namespace Crateside.Core.Staging;

/// <summary>
/// What Live shows of a staged stem's state: the clip colour of its kind and song, and the browser
/// Collections its warp and verdict call for (#130).
/// </summary>
public sealed class StagingMarks(
    CratesidePaths paths,
    StemWarpReader warps,
    SidecarWriter sidecars,
    LiveAnalyses waiting,
    LedgerStore store,
    FolderInfoReader reader,
    FolderInfoWriter writer
)
{
    /// <summary>Gives every staged stem's saved sidecar its clip colour, and returns the ones rewritten.</summary>
    public IReadOnlyList<string> Recolour() =>
        [.. Staged().Where(path => waiting.Of(path) is null && sidecars.Recolour(path))];

    /// <summary>Puts every staged file in the Collections it belongs in, in one write.</summary>
    public TagWriteOutcome Collect()
    {
        FolderInfo existing;

        try
        {
            existing = reader.Read(paths.StagingFolder);
        }
        // Read as empty, every file would lose the Collections the user put it in.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TagWriteOutcome.Stale(ex.Message);
        }

        var ledger = store.Read();

        List<TagAssignment> assignments =
        [
            .. Staged()
                .Select(path =>
                {
                    var has = existing.TryGet(path, out var tags) ? tags.Collections : [];
                    var wanted = Wanted(warps.Read(path), ledger, has);

                    return has.SequenceEqual(wanted)
                        ? null
                        : new TagAssignment
                        {
                            RelativePath = Path.GetRelativePath(paths.StagingFolder, path),
                            Collections = wanted,
                        };
                })
                .OfType<TagAssignment>(),
        ];

        return assignments.Count is 0
            ? new TagWriteOutcome.Written(0)
            : TagWriteOutcome.Of(() => writer.Apply(paths.StagingFolder, assignments));
    }

    private static IReadOnlyList<int> Wanted(
        StemWarp stem,
        KeyLedger ledger,
        IReadOnlyList<int> has
    )
    {
        LedgerRun? verdict = null;

        if (StemTagReader.Read(stem.Path)?.SourceVideoId is { } videoId)
            ledger.TryGetLatestByVideoId(videoId, out verdict);

        var marks = BrowserCollections.TrackMarksFor(verdict);

        // A stem whose warp is with Live keeps the warp state it had until Live is done.
        return WarpStateOf(stem) is { } state
            ? BrowserCollections.Merged(has, [state, .. marks], BrowserCollections.Cratesides)
            : BrowserCollections.Merged(has, marks, BrowserCollections.TrackMarks);
    }

    private static BrowserCollection? WarpStateOf(StemWarp stem) =>
        stem switch
        {
            { Owner: WarpOwner.WaitingForLive or WarpOwner.PuttingBack } => null,
            { Owner: WarpOwner.Starter } => BrowserCollection.CheckBeat,
            { Owner: WarpOwner.Yours or WarpOwner.Borrowed } => BrowserCollection.Warped,
            _ => BrowserCollection.WarpByHand,
        };

    private IEnumerable<string> Staged() =>
        Directory.Exists(paths.StagingFolder)
            ? Directory
                .EnumerateFiles(paths.StagingFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(SampleFiles.IsAudio)
            : [];
}
