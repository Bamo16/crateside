using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Tracks;

/// <summary>
/// The playlist as tracks: one row each, whatever state they are in.
/// </summary>
/// <remarks>
/// Where <see cref="QueueView"/> sorts the playlist into the work lists each stage reads, this
/// puts it back together again — the same tracks, addressed by identity rather than by which
/// stage wants them next. Both exist because they answer different questions: a stage asks "what
/// may I pick up", and a person asks "where is my track".
/// </remarks>
public sealed record TracksView
{
    public required IReadOnlyList<TrackRow> Rows { get; init; }

    /// <summary>
    /// When the playlist behind these rows was last fetched. Null if it never has been.
    /// </summary>
    /// <remarks>
    /// Set by whoever assembled the read rather than by <see cref="Compose"/>, which knows nothing
    /// about clocks and is kept that way.
    /// </remarks>
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
    /// The user's standing instructions, by video ID. Read here rather than off the queue item,
    /// because a row outlives its playlist entry and the instructions outlive it too — a release
    /// with no vocals does not acquire them when the entry is deleted.
    /// </param>
    /// <param name="finishedRoot">
    /// The folder that means the user is done with a file — <c>Mashup Samples</c>. Passed rather
    /// than assumed so this stays pure and testable (ADR 0007).
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
        // The work lists become values on a row rather than the row's address. A stage is right
        // to ask "what may I pick up"; the page asks "where is my track", and answering that by
        // membership is what made a row move every time its state changed.
        var held = VideoIdsOf(view.Held);
        var parked = VideoIdsOf(view.AwaitingKey);

        // Separation is strictly one at a time, so "when" is honestly a position rather than a
        // time. Counted from one, in the order the selector will take them.
        var gpuLine = view
            .AwaitingSeparation.Select((item, index) => (item.VideoId, Position: index + 1))
            .ToDictionary(entry => entry.VideoId, entry => entry.Position, StringComparer.Ordinal);

        // The board is the only source carrying per-file tag state, so staged files come from it
        // rather than from the scan, even though the scan saw the same files.
        var staged = staging
            .Where(song => song.SourceVideoId is not null)
            .ToDictionary(song => song.SourceVideoId!, StringComparer.Ordinal);

        // Everything in the playlist, and then everything still in staging that is not.
        //
        // A track leaves the playlist the moment it is fully processed, which is long before the
        // user has finished with it: its stems sit in Sample Staging for weeks afterwards, waiting
        // to be warped. Building rows from the playlist alone lost every one of those — the
        // warping worklist is the folder, and the folder outlives the playlist entry.
        var queued = VideoIdsOf(view.Items);
        var reopened = VideoIdsOf(view.AwaitingSeparation.Where(i => i.PlaylistItemId is null));

        return new TracksView
        {
            Rows = InOrder([
                .. view.Items.Select(item => Row(item.VideoId, item.Title, item)),
                .. staging
                    .Where(song => song.SourceVideoId is { } id && !queued.Contains(id))
                    .Select(song => Row(song.SourceVideoId!, song.DisplayName)),
                // And a track being re-cut, which for a moment is in neither: its staged files
                // have just been deleted and the playlist let it go long ago. Without this the row
                // vanishes at exactly the point there is most reason to watch it.
                .. view
                    .AwaitingSeparation.Where(item =>
                        item.PlaylistItemId is null
                        && !queued.Contains(item.VideoId)
                        && !staged.ContainsKey(item.VideoId)
                    )
                    .Select(item => Row(item.VideoId, item.Title, item)),
                // And everything the library has that none of the above accounts for: tracks
                // keytag judged before crateside existed, and tracks finished with long enough ago
                // that the playlist entry is gone. Reachable under Done and nowhere else.
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

            // Two hops, never a direct lookup: a video ID resolves to a song identity and the
            // identity resolves to the latest run, which is what makes a supersede win rather than
            // the line that happened to name this URL.
            var verdict = ledger.TryGetLatestByVideoId(videoId, out var run) ? run : null;

            // The playlist's title while the track is still in it, and the bare video ID once
            // neither the playlist nor staging has anything to offer.
            var (derived, source) = NameOf(
                fallbackName,
                item is null && song is null ? TrackNameSource.VideoId : TrackNameSource.Playlist,
                verdict,
                song
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
                // The work list for the queue item, the instruction for everything else. They
                // agree while a track is in the playlist and only the instruction survives it.
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
                // The only finding derivable from the board alone: Ableton holding a key that is
                // not the ledger's current verdict, which is what a supersede leaves behind.
                AuditNote = song?.StaleTaggedStems.Count > 0 ? "tag disagrees" : null,
                Folder = FolderOf(song, files),
                StagedSince = StagedSinceOf(files, finishedRoot),
            };
        }
    }

    /// <summary>
    /// Names compared the way Ableton's browser compares them, so a warping session can scan the
    /// two lists side by side. Ordinal, which is what puts the Japanese titles last in both.
    /// </summary>
    private static readonly StringComparer ByName = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The default order: grouped by what the row wants from the user, then by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A row moves at most twice in its life, and only when that is the news.</strong> The
    /// page was built on rows never moving, because the thing it replaced bucketed tracks into
    /// sections and shuffled them between the two while you were trying to click one. The states a
    /// track crosses while crateside works it — queued through tagging — are therefore one group,
    /// so nothing moves as it goes. <em>Held is in that group too</em>, which matters most: a hold
    /// is a change the user makes, and having the row jump out from under the press was the worst
    /// of it.
    /// </para>
    /// <para>
    /// What does move a row is reaching <em>ready to warp</em>, being parked for want of a key, or
    /// being finished with — the three transitions worth noticing, and the only three.
    /// </para>
    /// <para>
    /// Within a group, by name — except the working group, which keeps the order it was assembled
    /// in. That is the playlist's own oldest-first order, which is the order the stages will take
    /// them, and it is preserved by sorting the groups separately rather than by a second key.
    /// </para>
    /// </remarks>
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
    /// What to call the track when the user has not said: the ledger's identity, then the board's,
    /// then the playlist's.
    /// </summary>
    /// <remarks>
    /// <strong>The playlist title is the worst of the three and is the fallback for that reason.</strong>
    /// It is whatever the uploader named the video, which routinely omits the artist — a row
    /// reading "99th Street" where the library holds "Starjunk 95 - 99th Street" is the same
    /// track under a name the user cannot search for. The ledger records artist and title
    /// separately and properly cased, which is what makes it the better source; its <c>Song</c>
    /// field is not used here because that is the normalized lowercase identity, built for
    /// matching rather than for reading.
    /// </remarks>
    private static (string Name, TrackNameSource Source) NameOf(
        string fallback,
        TrackNameSource fallbackSource,
        LedgerRun? verdict,
        StagingSong? staged
    ) =>
        (verdict, staged) switch
        {
            ({ Artist.Length: > 0, Title.Length: > 0 }, _) => (
                verdict.DisplayName,
                TrackNameSource.Ledger
            ),
            (_, { DisplayName.Length: > 0 }) => (staged.DisplayName, TrackNameSource.StagingBoard),
            _ => (fallback, fallbackSource),
        };

    /// <summary>
    /// Staged files a rename has not reached.
    /// </summary>
    /// <remarks>
    /// Counted only against a name the user stated. A <em>derived</em> name routinely differs from
    /// what StemForge wrote — the ledger identifies a song and the uploader named a video — so
    /// measuring against one would flag most of the library as half-renamed.
    /// </remarks>
    private static int AwaitingRenameIn(StagingSong? staged, string? stated) =>
        staged is null || !StemFileName.IsUsable(stated)
            ? 0
            : staged.Stems.Count(stem =>
                !StemFileName
                    .Renamed(stem.Path, stated)
                    .Equals(stem.Path, StringComparison.OrdinalIgnoreCase)
            );

    /// <summary>
    /// A track's kept files, from wherever they currently are.
    /// </summary>
    /// <remarks>
    /// Two sources because staging is emptied progressively and they overlap for as long as the
    /// user takes: the board holds what is still in <c>Sample Staging</c> and the tags on it,
    /// while the scan finds what has reached <c>Mashup Samples</c>. A file is under exactly one
    /// root, so nothing is counted twice.
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
                    // AbletonKey is left null, and the omission is the point: crateside
                    // never writes tags in Mashup Samples, and that store is not a faithful mirror
                    // of what Ableton holds (ADR 0003), so nothing here has read this file's tag.
                    // Which folder it is in is the honest claim, and it is the one that matters:
                    // the move out of staging is the user finishing with the file, tagging and
                    // warping and renaming together.
                }),
        ];

    /// <summary>
    /// The folder the track's files are in: staging while anything is still there, else wherever
    /// they have got to.
    /// </summary>
    /// <remarks>
    /// Taken from a file rather than assembled from a root and a name, because the layout under a
    /// root belongs to whoever put the files there and a rebuilt path would be a second opinion
    /// about it.
    /// </remarks>
    private static string? FolderOf(StagingSong? staged, IReadOnlyList<StemOnDisk> onDisk) =>
        (staged?.Stems.FirstOrDefault()?.Path ?? onDisk.FirstOrDefault()?.Path) is { } path
            ? Path.GetDirectoryName(path)
            : null;

    /// <summary>
    /// When the track arrived in staging: the earliest of the files still there.
    /// </summary>
    /// <remarks>
    /// Earliest because handoff moves a track's files together, so the oldest of them is when the
    /// track landed. Files that have reached the finished root are excluded — they are no longer
    /// staged, and a track with none left is not staged since anything.
    /// </remarks>
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
