using Crateside.Core.Ledger;
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
    public static TracksView Compose(
        QueueView view,
        KeyLedger ledger,
        IReadOnlyList<StagingSong> staging,
        IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> onDisk,
        string finishedRoot,
        JobStore jobs,
        IReadOnlyDictionary<string, TrackInstruction> instructions
    )
    {
        var held = VideoIdsOf(view.Held);
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

        return new TracksView
        {
            Rows = InOrder([
                .. view.Items.Select(item => Row(item.VideoId, item.Title, item)),
                .. staging
                    .Where(song => song.SourceVideoId is { } id && !queued.Contains(id))
                    .Select(song => Row(song.SourceVideoId!, song.DisplayName)),
                // A track being re-cut: staged files just deleted, playlist entry long gone.
                .. view
                    .AwaitingSeparation.Where(item =>
                        item.PlaylistItemId is null
                        && !queued.Contains(item.VideoId)
                        && !staged.ContainsKey(item.VideoId)
                    )
                    .Select(item => Row(item.VideoId, item.Title, item)),
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
                    .Select(entry => Row(entry.Key, entry.Key) with { IsLibraryOnly = true }),
            ]),
        };

        TrackRow Row(string videoId, string fallbackName, QueueItem? item = null)
        {
            var song = staged.GetValueOrDefault(videoId);
            var files = onDisk.GetValueOrDefault(videoId) ?? [];
            var instruction = instructions.GetValueOrDefault(videoId);
            var progress = StageProgress.For(jobs, videoId);

            // Resolves through song identity so a supersede wins over whichever run named this
            // URL.
            var verdict = ledger.TryGetLatestByVideoId(videoId, out var run) ? run : null;

            var (derived, source) = NameOf(
                fallbackName,
                item is null && song is null ? TrackNameSource.VideoId : TrackNameSource.Playlist,
                NameFromFiles(song, files),
                verdict
            );

            var stated = instruction?.Name is { Length: > 0 } name ? name : null;

            return new TrackRow
            {
                VideoId = videoId,
                PlaylistItemId = item?.PlaylistItemId,
                Title = stated ?? derived,
                DerivedTitle = stated is null ? null : derived,
                NameSource = source,
                AwaitingRename = AwaitingRenameIn(song, stated),
                Verdict = verdict,
                // The work list covers the queue item; the instruction covers everything after it
                // leaves the playlist.
                IsHeld = held.Contains(videoId) || instruction?.IsHeld is true,
                IsInstrumentalRelease = instruction?.IsInstrumentalRelease is true,
                IsParkedForKey = parked.Contains(videoId),
                GpuPosition = gpuLine.TryGetValue(videoId, out var position) ? position : null,
                RunningStage = progress.Running,
                RunningSince = progress.RunningSince,
                GaveUpAt = progress.GaveUpAt,
                GaveUpStrikes = progress.GaveUpStrikes,
                Samples = SamplesOf(song, files, finishedRoot),
                HasWarpGuide = song?.WarpGuides.Count > 0,
                AuditNote = song?.StaleTaggedStems.Count > 0 ? "tag disagrees" : null,
                Folder = FolderOf(song, files),
                StagedSince = StagedSinceOf(files, finishedRoot),
            };
        }
    }

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
        LedgerRun? verdict
    ) =>
        (onDisk, verdict) switch
        {
            ({ Length: > 0 }, _) => (onDisk, TrackNameSource.Files),
            (_, { Artist.Length: > 0, Title.Length: > 0 }) => (
                verdict.DisplayName,
                TrackNameSource.Ledger
            ),
            _ => (fallback, fallbackSource),
        };

    /// <summary>
    /// What this track's files are called with the preset stripped, which is what Ableton shows.
    /// </summary>
    /// <remarks>Settled by the most common name among the files, not by whichever was read first.</remarks>
    private static string? NameFromFiles(StagingSong? staged, IReadOnlyList<StemOnDisk> onDisk) =>
        (staged?.Stems.Select(stem => stem.Path) ?? onDisk.Select(file => file.Path))
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
        string finishedRoot
    ) =>
        [
            .. staged?.Samples.Select(sample => new TrackSample
            {
                Label = sample.Label,
                AbletonKey = sample.AbletonKey,
            }) ?? [],
            .. onDisk
                .Where(file =>
                    file.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase)
                    && !StagingStem.IsWarpGuidePreset(file.Preset)
                )
                .Select(file => new TrackSample
                {
                    Label = file.Preset ?? "source",
                    IsInLibrary = true,
                }),
        ];

    /// <summary>
    /// The folder the track's files are in: staging while anything is still there, else wherever
    /// they have got to.
    /// </summary>
    private static string? FolderOf(StagingSong? staged, IReadOnlyList<StemOnDisk> onDisk) =>
        (staged?.Stems.FirstOrDefault()?.Path ?? onDisk.FirstOrDefault()?.Path) is { } path
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
