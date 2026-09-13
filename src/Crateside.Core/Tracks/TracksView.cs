using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Tracks;

/// <summary>
/// The playlist as tracks: one row each, whatever state they are in.
/// </summary>
/// <remarks>
/// Where <see cref="QueueView"/> sorts the playlist into per-stage work lists, this puts it back
/// together by track identity.
/// </remarks>
public sealed record TracksView
{
    public required IReadOnlyList<TrackRow> Rows { get; init; }

    /// <summary>
    /// When the playlist behind these rows was last fetched. Null if it never has been.
    /// </summary>
    /// <remarks>Set by the caller, not by <see cref="Compose"/>, which knows nothing about clocks.</remarks>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>
    /// Joins everything known about the playlist into one row per track.
    /// </summary>
    /// <param name="staging">
    /// The staging board, which carries per-file tag state that no other source has.
    /// </param>
    /// <param name="onDisk">
    /// Every file carrying a track's provenance, by video ID, across the roots that were scanned.
    /// </param>
    /// <param name="instructions">
    /// The user's standing instructions, by video ID — read here rather than off the queue item
    /// because they outlive the playlist entry too.
    /// </param>
    /// <param name="finishedRoot">
    /// The folder that means the user is done with a file — <c>Mashup Samples</c>.
    /// </param>
    /// <param name="acquisitions">
    /// How each retained download turned out, by video ID — the measured premium outcome a row
    /// is parked over.
    /// </param>
    public static TracksView Compose(
        QueueView view,
        KeyLedger ledger,
        IReadOnlyList<StagingSong> staging,
        IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> onDisk,
        string finishedRoot,
        JobStore jobs,
        IReadOnlyDictionary<string, TrackInstruction> instructions,
        IReadOnlyDictionary<string, ResolvedName>? resolvedNames = null,
        IReadOnlyDictionary<string, Acquisition>? acquisitions = null,
        PresetCatalog? presets = null
    )
    {
        var resolved = resolvedNames ?? new Dictionary<string, ResolvedName>();
        var catalog = presets ?? PresetCatalog.Empty;
        var acquired = acquisitions ?? new Dictionary<string, Acquisition>();

        var parked = VideoIdsOf(view.AwaitingKey);

        var gpuLine = view
            .AwaitingSeparation.Select((item, index) => (item.VideoId, Position: index + 1))
            .ToDictionary(entry => entry.VideoId, entry => entry.Position, StringComparer.Ordinal);

        var staged = staging
            .Where(song => song.SourceVideoId is not null)
            .ToDictionary(song => song.SourceVideoId!, StringComparer.Ordinal);

        // Everything in the playlist, then everything still in staging that isn't.
        var queued = VideoIdsOf(view.Items);
        var reopened = VideoIdsOf(view.AwaitingSeparation.Where(i => i.PlaylistItemId is null));

        // One seed per video ID with something to say. A song that has been cut from more than
        // one upload contributes several, and they are folded into one row below (ADR 0014).
        List<RowSeed> seeds =
        [
            .. view.Items.Select(item => new RowSeed(item.VideoId, item.Title, item)),
            .. staging
                .Where(song => song.SourceVideoId is { } id && !queued.Contains(id))
                .Select(song => new RowSeed(song.SourceVideoId!, song.DisplayName)),
            // A track being re-cut: staged files just deleted, playlist entry long gone.
            .. view
                .AwaitingSeparation.Where(item =>
                    item.PlaylistItemId is null
                    && !queued.Contains(item.VideoId)
                    && !staged.ContainsKey(item.VideoId)
                )
                .Select(item => new RowSeed(item.VideoId, item.Title, item)),
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
                        seed => ledger.SongFor(seed.VideoId) ?? seed.VideoId,
                        StringComparer.Ordinal
                    )
                    .Select(song => Row([.. song])),
            ]),
        };

        TrackRow Row(IReadOnlyList<RowSeed> members)
        {
            // Resolves through song identity, so every upload of this song lands on the same
            // verdict and a supersede wins over whichever run named a given URL.
            var verdict = ledger.TryGetLatestByVideoId(members[0].VideoId, out var run)
                ? run
                : null;

            // The upload the row speaks for: the one still in the playlist, else the one the
            // ledger's latest run was cut from, else whichever came first.
            var primary =
                members.FirstOrDefault(seed => seed.Item is not null)
                ?? members.FirstOrDefault(seed =>
                    seed.VideoId.Equals(verdict?.SourceVideoId, StringComparison.Ordinal)
                )
                ?? members[0];

            var songs = members
                .Select(seed => staged.GetValueOrDefault(seed.VideoId))
                .OfType<StagingSong>()
                .ToArray();

            var files = members
                .SelectMany(seed => onDisk.GetValueOrDefault(seed.VideoId) ?? [])
                .ToArray();

            // One instruction, never a merge of several: the primary's if it has one, else
            // whichever upload of this song carries one.
            var instruction =
                instructions.GetValueOrDefault(primary.VideoId)
                ?? members
                    .Select(seed => instructions.GetValueOrDefault(seed.VideoId))
                    .OfType<TrackInstruction>()
                    .FirstOrDefault();

            // Sources the plan names are included even before their files exist: an added one
            // carries a video ID of its own, and until it lands nothing else on the row is
            // holding its download open (ADR 0019).
            var progress = StageProgress.For(
                jobs,
                [
                    .. members.Select(seed => seed.VideoId),
                    .. instruction?.Plan?.AddedSources.Select(source => source.VideoId) ?? [],
                ]
            );

            // The primary's download, since that is the upload the row currently speaks for.
            var acquisition =
                acquired.GetValueOrDefault(primary.VideoId)
                ?? members
                    .Select(seed => acquired.GetValueOrDefault(seed.VideoId))
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
                    .Select(seed => resolved.GetValueOrDefault(seed.VideoId)?.DisplayTitle)
                    .FirstOrDefault(name => name is { Length: > 0 })
            );

            var stated = instruction?.Name is { Length: > 0 } name ? name : null;

            return new TrackRow
            {
                VideoId = primary.VideoId,
                PlaylistItemId = primary.Item?.PlaylistItemId,
                Title = stated ?? derived,
                DerivedTitle = stated is null ? null : derived,
                NameSource = source,
                StatedSourceUrl = instruction?.SourceUrl,
                AwaitingRename = songs.Sum(song => AwaitingRenameIn(song, stated)),
                Verdict = verdict,
                // The work list covers the queue item; the instruction covers everything after it
                // leaves the playlist.
                Plan = instruction?.Plan ?? StemPlan.Default,
                // Want of a key outranks a shortfall: it is the later stage, and the GPU has
                // already been spent by the time a track can be in it.
                ParkedFor =
                    members.Any(seed => parked.Contains(seed.VideoId)) ? ParkReason.NoKey
                    : acquisition?.Stops is true ? ParkReason.PremiumShortfall
                    : null,
                Acquisition = acquisition,
                GpuPosition = members
                    .Select(seed =>
                        gpuLine.TryGetValue(seed.VideoId, out var position) ? position : (int?)null
                    )
                    .Min(),
                RunningStage = progress.Running,
                RunningSince = progress.RunningSince,
                GaveUpAt = progress.GaveUpAt,
                GaveUpStrikes = progress.GaveUpStrikes,
                Samples =
                [
                    .. members.SelectMany(seed =>
                        SamplesOf(
                            staged.GetValueOrDefault(seed.VideoId),
                            onDisk.GetValueOrDefault(seed.VideoId) ?? [],
                            finishedRoot,
                            seed.VideoId,
                            catalog
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
                Inventory = StemInventory.Of(files, catalog),
            };
        }
    }

    /// <summary>One upload's contribution to a row, before a song's uploads are folded together.</summary>
    private sealed record RowSeed(
        string VideoId,
        string FallbackName,
        QueueItem? Item = null,
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
            { State: TrackState.Parked or TrackState.GaveUp }
            or { AuditNote: not null }
            or { AwaitingRename: > 0 } => 0,
            { State: TrackState.ReadyToWarp } => 2,
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
    /// What this track's files are called with the preset stripped, which is what Ableton shows.
    /// </summary>
    /// <remarks>Settled by the most common name among the files, not by whichever was read first.</remarks>
    private static string? NameFromFiles(
        IReadOnlyList<StagingSong> staged,
        IReadOnlyList<StemOnDisk> onDisk
    ) =>
        (
            staged.Count > 0
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
    private static int AwaitingRenameIn(StagingSong? staged, string? stated) =>
        staged is null || !StemFileName.IsUsable(stated)
            ? 0
            : staged.Stems.Count(stem =>
                !StemFileName
                    .Renamed(stem.Path, stated)
                    .Equals(stem.Path, StringComparison.OrdinalIgnoreCase)
            );

    /// <summary>A track's kept files, from wherever they currently are.</summary>
    /// <remarks>
    /// A file is under exactly one root, so combining the two sources never double-counts.
    /// </remarks>
    private static IReadOnlyList<TrackSample> SamplesOf(
        StagingSong? staged,
        IReadOnlyList<StemOnDisk> onDisk,
        string finishedRoot,
        string sourceVideoId,
        PresetCatalog catalog
    ) =>
        [
            .. staged?.Samples.Select(sample =>
                Sample(StemOnDisk.Of(sample), catalog, sourceVideoId) with
                {
                    AbletonKey = sample.AbletonKey,
                }
            )
                ?? [],
            .. onDisk
                .Where(file =>
                    file.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase)
                    && !StagingStem.IsWarpGuidePreset(file.Preset)
                )
                .Select(file => Sample(file, catalog, sourceVideoId) with { IsInLibrary = true }),
        ];

    /// <summary>
    /// One file, said in both vocabularies: what the separator called it, and what the plan would
    /// ask for to make it again.
    /// </summary>
    private static TrackSample Sample(StemOnDisk file, PresetCatalog catalog, string sourceVideoId)
    {
        var classified = ClassifiedStem.Of(file, catalog);

        return new TrackSample
        {
            Label = classified.DisplayName ?? "source",
            Kind = classified.Kind,
            PresetId = classified.PresetId,
            SourceVideoId = sourceVideoId,
        };
    }

    /// <summary>
    /// The folder the track's files are in: staging while anything is still there, else wherever
    /// they have got to.
    /// </summary>
    private static string? FolderOf(
        IReadOnlyList<StagingSong> staged,
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

    private static HashSet<string> VideoIdsOf(IEnumerable<QueueItem> items) =>
        items.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);
}
