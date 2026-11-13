using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Samples;

namespace Crateside.Core.Staging;

/// <summary>
/// Gives each kept warp guide its track's mark Collections, so a drum stem searched in Live shows
/// what the track's other stems do (#147).
/// </summary>
public sealed class KeptStemMarks(
    CratesidePaths paths,
    LedgerStore store,
    TrackMarkStore trackMarks,
    FolderInfoReader reader,
    FileIndexReader index,
    FolderInfoWriter writer,
    TagStoreBackups backups,
    CollectionSettingsStore collections,
    MeterStore? meters = null
)
{
    public TagWriteOutcome Collect()
    {
        if (!Directory.Exists(paths.KeptWarpGuides))
            return new TagWriteOutcome.Written(0);

        FolderInfo existing;
        FolderInfo indexed;

        try
        {
            existing = reader.Read(paths.KeptWarpGuides);
            indexed = index.Read(paths.KeptWarpGuides);
        }
        // Read as empty, every file would lose what Live holds for it.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TagWriteOutcome.Stale(ex.Message);
        }

        var ledger = store.Read();
        var marked = trackMarks.Read();
        var settings = collections.Read();

        var files = Directory
            .EnumerateFiles(paths.KeptWarpGuides, "*", SearchOption.TopDirectoryOnly)
            .Where(SampleFiles.IsAudio)
            .Select(path =>
            {
                var stored = existing.TryGet(path, out var entry) ? entry : null;
                var live = indexed.TryGet(path, out var seen) ? seen : null;
                var has = stored?.Collections ?? live?.Collections ?? [];

                return new
                {
                    Path = path,
                    Stored = stored,
                    Live = live,
                    Has = has,
                    Wanted = settings.Merged(
                        has,
                        MarksOf(path, ledger, marked, meters),
                        BrowserCollections.TrackMarks
                    ),
                };
            })
            .ToList();

        if (files.All(file => file.Has.SequenceEqual(file.Wanted)))
            return new TagWriteOutcome.Written(0);

        // A write reconciles the whole root to the store, so what Live holds for a file with no
        // entry has to be written back with the rest or it is lost (ADR 0023).
        var unbacked = files
            .Where(file => file.Stored is null && file.Live is { Keywords.Count: > 0 })
            .SelectMany(file => file.Live!.Keywords)
            .ToList();

        if (unbacked.Any(keyword => !CratesideKeywords.IsKnown(keyword)))
            return new TagWriteOutcome.Rejected(
                "a kept stem carries a tag crateside cannot write, so writing the folder would lose it"
            );

        List<TagAssignment> assignments =
        [
            .. files
                .Where(file =>
                    !file.Has.SequenceEqual(file.Wanted)
                    || (file.Stored is null && file.Live is { Keywords.Count: > 0 })
                )
                .Select(file => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(paths.KeptWarpGuides, file.Path),
                    Keywords = file.Stored is null ? file.Live?.Keywords : null,
                    Collections = file.Wanted,
                }),
        ];

        backups.Save(paths.KeptWarpGuides);

        return TagWriteOutcome.Of(() => writer.Apply(paths.KeptWarpGuides, assignments));
    }

    private static IReadOnlyList<BrowserCollection> MarksOf(
        string path,
        KeyLedger ledger,
        IReadOnlyDictionary<string, TrackMarkRecord> marked,
        MeterStore? meters
    )
    {
        LedgerRun? verdict = null;
        IReadOnlyCollection<TrackMark> mine = [];

        if (StemTagReader.Read(path)?.SourceTrackKey is { } trackKey)
        {
            ledger.TryGetLatestByTrackKey(trackKey, out verdict);
            var song = verdict?.Song ?? trackKey;

            mine = marked.GetValueOrDefault(song)?.Marks ?? [];
            mine = meters?.MarksFor(song, mine) ?? mine;
        }

        return BrowserCollections.TrackMarksFor(verdict, mine);
    }
}
