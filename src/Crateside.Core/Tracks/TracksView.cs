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
    /// Joins everything known about the playlist into one row per track.
    /// </summary>
    /// <param name="staging">
    /// The staging board, which carries per-file tag state that no other source has.
    /// </param>
    /// <param name="onDisk">
    /// Every file carrying a track's provenance, by video ID, across the roots that were scanned.
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
        JobStore jobs
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

        return new TracksView
        {
            Rows = [.. view.Items.Select(item => Row(item, StageProgress.For(jobs, item.VideoId)))],
        };

        TrackRow Row(QueueItem item, StageProgress progress)
        {
            var song = staged.GetValueOrDefault(item.VideoId);

            return new TrackRow
            {
                VideoId = item.VideoId,
                PlaylistItemId = item.PlaylistItemId,
                Title = item.Title,
                // Two hops, never a direct lookup: a video ID resolves to a song identity and the
                // identity resolves to the latest run, which is what makes a supersede win rather
                // than the line that happened to name this URL.
                Verdict = ledger.TryGetLatestByVideoId(item.VideoId, out var verdict)
                    ? verdict
                    : null,
                IsHeld = held.Contains(item.VideoId),
                IsParkedForKey = parked.Contains(item.VideoId),
                GpuPosition = gpuLine.TryGetValue(item.VideoId, out var position) ? position : null,
                RunningStage = progress.Running,
                GaveUpAt = progress.GaveUpAt,
                GaveUpStrikes = progress.GaveUpStrikes,
                Samples = SamplesOf(
                    song,
                    onDisk.GetValueOrDefault(item.VideoId) ?? [],
                    finishedRoot
                ),
                HasWarpGuide = song?.WarpGuides.Count > 0,
            };
        }
    }

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
                IsKeyTagged = sample.IsKeyTagged,
            }) ?? [],
            .. onDisk
                .Where(file =>
                    file.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase)
                    && !StagingStem.IsWarpGuidePreset(file.Preset)
                )
                .Select(file => new TrackSample
                {
                    Label = file.Preset ?? "source",
                    IsWarped = true,
                    // IsKeyTagged is left at its default, and the omission is the point: crateside
                    // never writes tags in Mashup Samples, and that store is not a faithful mirror
                    // of what Ableton holds (ADR 0003), so nothing here has read this file's tag.
                    // Saying it is warped is the honest claim, and it is the one that matters —
                    // the move covers tagging along with everything else.
                }),
        ];

    private static HashSet<string> VideoIdsOf(IEnumerable<QueueItem> items) =>
        items.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);
}
