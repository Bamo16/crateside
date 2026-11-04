using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Plans;
using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Tracks;

/// <summary>
/// The playlist as tracks: one row each, whatever state they are in — the work lists of
/// <see cref="WorkListsView"/> put back together by track identity.
/// </summary>
public sealed record TracksView
{
    public required IReadOnlyList<TrackRow> Rows { get; init; }

    /// <summary>
    /// When the playlist behind these rows was last fetched, set by the caller. Null if it never
    /// has been.
    /// </summary>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>
    /// Why the playlist could not be read, or null when it could. Shown beside the rows, which
    /// stand either way (ADR 0021).
    /// </summary>
    public string? PlaylistProblem { get; init; }

    /// <summary>
    /// Joins everything known about the playlist into one row per track.
    /// </summary>
    /// <param name="staging">
    /// What is in Staging, which carries per-file tag state that no other source has.
    /// </param>
    /// <param name="instructions">
    /// The user's standing instructions, by video ID; they outlive the playlist entry.
    /// </param>
    /// <param name="finishedRoot">
    /// The folder that means the user is done with a file — the library.
    /// </param>
    public static TracksView Compose(
        WorkListsView view,
        KeyLedger ledger,
        IReadOnlyList<StagedSong> staging,
        IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> onDisk,
        string finishedRoot,
        JobStore jobs,
        IReadOnlyDictionary<string, TrackInstruction> instructions,
        TrackAnnotations? annotations = null,
        SampleReadings? readings = null
    )
    {
        var known = annotations ?? TrackAnnotations.None;
        var read = readings ?? SampleReadings.None;

        var parked = TrackKeysOf(view.AwaitingKey);
        var unjudged = TrackKeysOf(view.Outstanding);

        // By song, not upload: a swap's old upload stops being a member once its files are gone.
        var deletedBySong = known
            .Deletions.Where(deletion => deletion.TrackKey is not null)
            .GroupBy(
                deletion => ledger.SongFor(deletion.TrackKey!) ?? deletion.TrackKey!,
                StringComparer.Ordinal
            )
            .ToDictionary(
                song => song.Key,
                song => (IReadOnlyList<Deletion>)[.. song.OrderByDescending(d => d.At)],
                StringComparer.Ordinal
            );

        // What the separation drainer will actually pick, so a row is never in a line it isn't in.
        var gpuLine = new SeparationSelector(jobs)
            .Line(view)
            .Select((item, index) => (item.TrackKey, Position: index + 1))
            .ToDictionary(entry => entry.TrackKey, entry => entry.Position, StringComparer.Ordinal);

        var staged = staging
            .Where(song => song.SourceTrackKey is not null)
            .ToDictionary(song => song.SourceTrackKey!, StringComparer.Ordinal);

        // Everything in the playlist, then everything still in staging that isn't.
        var queued = TrackKeysOf(view.Items);
        var reopened = TrackKeysOf(view.Reopened);

        // One seed per video ID with something to say. A song that has been cut from more than
        // one upload contributes several, and they are folded into one row below (ADR 0014).
        List<RowSeed> seeds =
        [
            .. view.Items.Select(item => new RowSeed(item.TrackKey, item.StandInTitle, item)),
            .. staging
                .Where(song => song.SourceTrackKey is { } id && !queued.Contains(id))
                .Select(song => new RowSeed(song.SourceTrackKey!, song.DisplayName)),
            // A track still owing a stage with its playlist entry long gone — a re-cut, or a
            // handoff or tagging left open. Read from Reopened rather than AwaitingSeparation so
            // a track that owes no cut keeps its row without joining the GPU line (#32).
            .. view
                .Reopened.Where(item =>
                    !queued.Contains(item.TrackKey) && !staged.ContainsKey(item.TrackKey)
                )
                .Select(item => new RowSeed(item.TrackKey, item.StandInTitle, item)),
            // Everything else the library has: tracks judged before crateside existed, or
            // finished long enough ago that the playlist entry is gone.
            .. onDisk
                .Where(entry =>
                    !queued.Contains(entry.Key)
                    && !staged.ContainsKey(entry.Key)
                    && !reopened.Contains(entry.Key)
                    && entry.Value.Any(file =>
                        file.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Select(entry => new RowSeed(entry.Key, entry.Key, IsLibraryOnly: true)),
        ];

        return new TracksView
        {
            Rows = InOrder([
                .. seeds
                    .GroupBy(
                        seed => ledger.SongFor(seed.TrackKey) ?? seed.TrackKey,
                        StringComparer.Ordinal
                    )
                    .Select(song => Row([.. song])),
            ]),
        };

        TrackRow Row(IReadOnlyList<RowSeed> members)
        {
            // Resolves through song identity, so every upload of this song lands on the same
            // verdict and a supersede wins over whichever run named a given URL.
            var verdict = ledger.TryGetLatestByTrackKey(members[0].TrackKey, out var run)
                ? run
                : null;

            // The upload the row speaks for: the one still in the playlist, else the one the
            // ledger's latest run was cut from, else whichever came first.
            var primary =
                members.FirstOrDefault(seed => seed.Item is not null)
                ?? members.FirstOrDefault(seed =>
                    seed.TrackKey.Equals(verdict?.SourceTrackKey, StringComparison.Ordinal)
                )
                ?? members[0];

            var songs = members
                .Select(seed => staged.GetValueOrDefault(seed.TrackKey))
                .OfType<StagedSong>()
                .ToArray();

            var files = members
                .SelectMany(seed => onDisk.GetValueOrDefault(seed.TrackKey) ?? [])
                .ToArray();

            // One instruction, never a merge of several: the primary's if it has one, else
            // whichever upload of this song carries one.
            var instruction =
                instructions.GetValueOrDefault(primary.TrackKey)
                ?? members
                    .Select(seed => instructions.GetValueOrDefault(seed.TrackKey))
                    .OfType<TrackInstruction>()
                    .FirstOrDefault();

            // Sources the plan names are included even before their files exist: an added one
            // carries a video ID of its own, and until it lands nothing else on the row is
            // holding its download open (ADR 0019).
            var progress = StageProgress.For(
                jobs,
                [
                    .. members.Select(seed => seed.TrackKey),
                    .. instruction?.Plan?.AddedSources.Select(source => source.TrackKey) ?? [],
                ]
            );

            var added = (instruction?.Plan?.AddedSources ?? [])
                .Select(source => source.TrackKey)
                .ToHashSet(StringComparer.Ordinal);

            // The primary's download, since that is the upload the row currently speaks for.
            // Never an added source's: its quality is a finding about that one file and does not
            // park the track (ADR 0019). The fallback used to reach one whenever the primary's
            // own acquisition had been discarded with its working set, which is every finished
            // track — so attaching a source to one put a shortfall banner across the whole row.
            var acquisition =
                known.Acquisitions.GetValueOrDefault(primary.TrackKey)
                ?? members
                    .Where(seed => !added.Contains(seed.TrackKey))
                    .Select(seed => known.Acquisitions.GetValueOrDefault(seed.TrackKey))
                    .OfType<Acquisition>()
                    .FirstOrDefault();

            var (derived, source) = NameOf(
                primary.FallbackName,
                primary.Item is null && songs.Length is 0
                    ? TrackNameSource.VideoId
                    : TrackNameSource.Playlist,
                NameFromFiles(songs, files),
                verdict,
                members
                    .Select(seed =>
                        known.ResolvedNames.GetValueOrDefault(seed.TrackKey)?.DisplayTitle
                    )
                    .FirstOrDefault(name => name is { Length: > 0 })
            );

            var stated = instruction?.Name is { Length: > 0 } name ? name : null;

            var isParked = members.Any(seed => parked.Contains(seed.TrackKey));
            var isUnjudged = members.Any(seed => unjudged.Contains(seed.TrackKey));

            return new TrackRow
            {
                TrackKey = primary.TrackKey,
                PlaylistItemId = primary.Item?.PlaylistItemId,
                Title = stated ?? derived,
                DerivedTitle = stated is null ? null : derived,
                NameSource = source,
                StatedSourceUrl = instruction?.SourceUrl,
                AwaitingRename = songs.Sum(song => AwaitingRenameIn(song, stated)),
                Verdict = verdict,
                Genre = known.Genres.GetValueOrDefault(verdict?.Song ?? primary.TrackKey),
                Familiarity = known.Familiarity.GetValueOrDefault(
                    verdict?.Song ?? primary.TrackKey
                ),
                Marks =
                    known.Marks.GetValueOrDefault(verdict?.Song ?? primary.TrackKey)?.Marks ?? [],
                Lookalike =
                    verdict is null && primary.Item is { } item
                        ? VerdictSelector.LookalikeOf(
                            item,
                            ledger,
                            known.ResolvedNames.GetValueOrDefault(item.TrackKey)?.DisplayTitle
                        )
                        : null,
                // The work list covers the queue item; the instruction covers everything after it
                // leaves the playlist.
                Plan = instruction?.Plan ?? StemPlan.Default,
                // No key is the verdict's outcome: a track never judged is waiting for the judge,
                // whatever folder its source was copied into (a source-only plan copies it into the
                // unresolved-key folder, #178). Want of a key outranks a shortfall once judged,
                // since the GPU has already been spent.
                ParkedFor =
                    isParked && !isUnjudged ? ParkReason.NoKey
                    : acquisition?.Stops is true ? ParkReason.PremiumShortfall
                    : null,
                WaitingForJudge =
                    isParked
                    && isUnjudged
                    && acquisition?.Stops is not true
                    && !(progress.AwaitingPerson && verdict is null),
                WaitingForPerson = progress.AwaitingPerson && verdict is null,
                Acquisition = acquisition,
                GpuPosition = members
                    .Select(seed =>
                        gpuLine.TryGetValue(seed.TrackKey, out var position) ? position : (int?)null
                    )
                    .Min(),
                RunningStage = progress.Running,
                RunningSince = progress.RunningSince,
                Deletions =
                    deletedBySong.GetValueOrDefault(
                        ledger.SongFor(members[0].TrackKey) ?? members[0].TrackKey
                    ) ?? [],
                GaveUpAt = progress.GaveUpAt,
                GaveUpStrikes = progress.GaveUpStrikes,
                GaveUpFor = progress.GaveUpFor,
                Retrying = progress.Retrying,
                Samples =
                [
                    .. members.SelectMany(seed =>
                        SamplesOf(
                            staged.GetValueOrDefault(seed.TrackKey),
                            onDisk.GetValueOrDefault(seed.TrackKey) ?? [],
                            finishedRoot,
                            seed.TrackKey,
                            read
                        )
                    ),
                ],
                HasWarpGuide = songs.Any(song => song.WarpGuides.Count > 0),
                AuditNote = songs.Any(song => song.StaleTaggedStems.Count > 0)
                    ? "tag disagrees"
                    : null,
                Folder = FolderOf(songs, files),
                StagedSince = StagedSinceOf(files, finishedRoot),
                IsLibraryOnly = members.All(seed => seed.IsLibraryOnly),
                IsAddedByUrl = instruction?.AddedByUrl is true,
                IsBackfill = instruction?.Backfill is true,
                Inventory = StemInventory.Of(files, read.Presets, finishedRoot),
            };
        }
    }

    /// <summary>One upload's contribution to a row, before a song's uploads are folded together.</summary>
    private sealed record RowSeed(
        string TrackKey,
        string FallbackName,
        WorkItem? Item = null,
        bool IsLibraryOnly = false
    );

    /// <summary>
    /// Compared the way Ableton's browser compares them — ordinal, so a warping session can scan
    /// both lists side by side.
    /// </summary>
    private static readonly StringComparer ByName = StringComparer.OrdinalIgnoreCase;

    /// <summary>The default order: grouped by what the row wants from the user, then by name.</summary>
    internal static IReadOnlyList<TrackRow> InOrder(IReadOnlyList<TrackRow> rows) =>
        [
            .. rows.Where(row => Group(row) is 0).OrderBy(row => row.Title, ByName),
            .. rows.Where(row => Group(row) is 1),
            .. rows.Where(row => Group(row) is 2).OrderBy(row => row.Title, ByName),
            .. rows.Where(row => Group(row) is 3).OrderBy(row => row.Title, ByName),
        ];

    private static int Group(TrackRow row) =>
        row switch
        {
            { State: TrackState.Parked or TrackState.GaveUp or TrackState.Short }
            or { AuditNote: not null }
            or { Lookalike: not null }
            or { WaitingForPerson: true }
            or { AwaitingRename: > 0 } => 0,
            { State: TrackState.ReadyToWarp or TrackState.ReadyToMove } => 2,
            { State: TrackState.Done } => 3,
            _ => 1,
        };

    /// <summary>
    /// What to call the track when the user has not: the files, then the ledger, then the
    /// playlist.
    /// </summary>
    private static (string Name, TrackNameSource Source) NameOf(
        string fallback,
        TrackNameSource fallbackSource,
        string? onDisk,
        LedgerRun? verdict,
        string? resolved
    ) =>
        (onDisk, verdict, resolved) switch
        {
            ({ Length: > 0 }, _, _) => (onDisk, TrackNameSource.Files),
            (_, { Artist.Length: > 0, Title.Length: > 0 }, _) => (
                verdict.DisplayName,
                TrackNameSource.Ledger
            ),
            (_, _, { Length: > 0 }) => (resolved, TrackNameSource.Resolved),
            _ => (fallback, fallbackSource),
        };

    /// <summary>
    /// The most common name among this track's files with the preset stripped, which is what
    /// Ableton shows.
    /// </summary>
    private static string? NameFromFiles(StagedSong[] staged, IReadOnlyList<StemOnDisk> onDisk) =>
        (
            staged.Length > 0
                ? staged.SelectMany(song => song.Stems).Select(stem => stem.Path)
                : onDisk.Select(file => file.Path)
        )
            .Select(StemFileName.WithoutPreset)
            .Where(name => name is { Length: > 0 })
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(names => names.Count())
            .ThenBy(names => names.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Key;

    /// <summary>
    /// Staged files a rename has not reached — measured against a stated name only, never a
    /// derived one, which routinely differs from what StemForge wrote.
    /// </summary>
    private static int AwaitingRenameIn(StagedSong? staged, string? stated) =>
        staged is null || !StemFileName.IsUsable(stated)
            ? 0
            : staged.Stems.Count(stem =>
                !StemFileName
                    .Renamed(stem.Path, stated)
                    .Equals(stem.Path, StringComparison.OrdinalIgnoreCase)
            );

    /// <summary>A track's kept files, from wherever they currently are.</summary>
    private static IReadOnlyList<TrackSample> SamplesOf(
        StagedSong? staged,
        IReadOnlyList<StemOnDisk> onDisk,
        string finishedRoot,
        string sourceTrackKey,
        SampleReadings readings
    ) =>
        [
            .. staged
                ?.Samples.Select(sample =>
                    (
                        sample,
                        written: sample.HasDefaultClip
                            ? readings.WrittenWarp?.Invoke(sample.Path)
                            : null
                    )
                )
                .Select(staged =>
                    Sample(StemOnDisk.Of(staged.sample), readings.Presets, sourceTrackKey) with
                    {
                        AbletonKey = staged.sample.AbletonKey,
                        // A starter warp is crateside's guess at the tempo, not a warp (#77), and a
                        // clip with Warp off is none at all (ADR 0059).
                        HasDefaultClip =
                            staged
                                is {
                                    sample.HasDefaultClip: true,
                                    written: null or { IsStarter: false, IsUnwarped: false },
                                },
                        Tempo = staged.sample.HasDefaultClip
                            ? readings.TempoOf?.Invoke(staged.sample.Path)
                            : null,
                        WarpBorrowedFrom = staged.written?.From,
                        Beats = readings.BeatsOf?.Invoke(
                            StemOnDisk.Of(staged.sample).SourceTrackKey ?? sourceTrackKey,
                            staged.sample.Path
                        ),
                    }
                )
                ?? [],
            .. onDisk
                .Where(file =>
                    file.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase)
                    && !StagedFile.IsWarpGuidePreset(file.Preset)
                )
                .Select(file =>
                    Sample(file, readings.Presets, sourceTrackKey) with
                    {
                        IsInLibrary = true,
                        AbletonKey = KeyIn(readings.LibraryTags, file.Path),
                        Tempo = readings.TempoOf?.Invoke(file.Path),
                        Beats = readings.BeatsOf?.Invoke(
                            file.SourceTrackKey ?? sourceTrackKey,
                            file.Path
                        ),
                    }
                ),
        ];

    /// <summary>
    /// What Ableton has tagged a library file with, which Staging cannot say.
    /// </summary>
    private static string? KeyIn(FolderInfo? tags, string path) =>
        tags?.TryGet(path, out var tagged) is true ? tagged.Key : null;

    /// <summary>
    /// One file, said in both vocabularies: what the separator called it, and what the plan would
    /// ask for to make it again.
    /// </summary>
    private static TrackSample Sample(StemOnDisk file, PresetCatalog catalog, string sourceTrackKey)
    {
        var classified = ClassifiedStem.Of(file, catalog);

        return new TrackSample
        {
            Path = file.Path,
            Label = classified.DisplayName ?? "source",
            Kind = classified.Kind,
            PresetId = classified.PresetId,
            // What the file says it was cut from, which is the seed only where it says nothing.
            // Reading the seed first made every file of a song report the same upload, so an
            // attached source could not be told from a stem cut alongside it.
            SourceTrackKey = file.SourceTrackKey ?? sourceTrackKey,
            IsPremium = file.IsPremium,
        };
    }

    /// <summary>
    /// The folder the track's files are in: staging while anything is still there, else wherever
    /// they have got to.
    /// </summary>
    private static string? FolderOf(
        IReadOnlyList<StagedSong> staged,
        IReadOnlyList<StemOnDisk> onDisk
    ) =>
        (
            staged.SelectMany(song => song.Stems).FirstOrDefault()?.Path
            ?? onDisk.FirstOrDefault()?.Path
        )
            is { } path
            ? Path.GetDirectoryName(path)
            : null;

    /// <summary>When the track arrived in staging: the earliest of the files still there.</summary>
    private static DateTimeOffset? StagedSinceOf(
        IReadOnlyList<StemOnDisk> onDisk,
        string finishedRoot
    ) =>
        onDisk
            .Where(file => !file.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase))
            .Select(file => file.CreatedAt)
            .OfType<DateTimeOffset>()
            .DefaultIfEmpty()
            .Min()
            is { Ticks: > 0 } earliest
            ? earliest
            : null;

    private static HashSet<string> TrackKeysOf(IEnumerable<WorkItem> items) =>
        items.Select(item => item.TrackKey).ToHashSet(StringComparer.Ordinal);
}
