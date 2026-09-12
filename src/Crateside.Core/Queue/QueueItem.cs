namespace Crateside.Core.Queue;

/// <summary>One entry in the Queue playlist.</summary>
public sealed record QueueItem
{
    /// <summary>
    /// The playlist item's own ID — what deletion targets. Distinct from the video ID: the
    /// same video added twice has two playlist items.
    /// </summary>
    /// <remarks>Null for a track crateside is working that the playlist no longer holds.</remarks>
    public string? PlaylistItemId { get; init; }

    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>The uploading channel, for telling two versions of a track apart.</summary>
    public string? ChannelTitle { get; init; }

    /// <summary>
    /// When the item was added to the playlist. This, not <see cref="Position"/>, is the queue
    /// order — "Add new videos to top of playlist" is a per-playlist setting, so position can
    /// mean opposite things across playlists.
    /// </summary>
    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>Where the item sits in the playlist. Display only; see <see cref="AddedAt"/>.</summary>
    public int? Position { get; init; }

    /// <summary>Whether this is likely to download at Premium quality.</summary>
    public PremiumOutlook PremiumOutlook { get; init; }

    /// <summary>
    /// True when the video is no longer playable — deleted or made private upstream. YouTube
    /// keeps the playlist entry and blanks its title, so it can never become processed and
    /// would otherwise sit in the queue forever.
    /// </summary>
    public bool IsUnavailable { get; init; }

    /// <summary>
    /// Which files the user wants made for this track.
    /// </summary>
    /// <remarks>
    /// Carried from the user's standing instruction, never inferred here. Defaults to
    /// <see cref="StemPlan.Default"/> for a track nobody has chosen for, so a reader never has to
    /// handle "not stated" separately.
    /// </remarks>
    public StemPlan Plan { get; init; } = StemPlan.Default;

    /// <summary>The upload to fetch instead of this entry's own video, or null.</summary>
    /// <remarks>
    /// <see cref="VideoId"/> still names the entry's original video and stays the join key.
    /// </remarks>
    public string? StatedSourceUrl { get; init; }

    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>The Queue as work: what is in the playlist, what is done, what is new.</summary>
public sealed record QueueView
{
    public required IReadOnlyList<QueueItem> Items { get; init; }

    /// <summary>
    /// Sorts the playlist into the work lists each stage reads.
    /// </summary>
    /// <param name="fullyProcessed">Video IDs with both a verdict and stems on disk.</param>
    /// <param name="verdictWithoutStems">Video IDs with a verdict and no stems anywhere.</param>
    /// <param name="separatedWithoutKey">
    /// Video IDs whose stems are parked in the unresolved-key root, separated already and
    /// waiting on a verdict good enough to tag with.
    /// </param>
    /// <param name="instructions">The user's standing instructions, keyed by video ID.</param>
    /// <param name="reopened">
    /// Tracks with separation to redo that the playlist no longer holds. Join
    /// <see cref="AwaitingSeparation"/> only — kept out of <see cref="Items"/> and
    /// <see cref="Outstanding"/> so a re-cut cannot cost quota.
    /// </param>
    public static QueueView Compose(
        IReadOnlyList<QueueItem> items,
        IReadOnlySet<string> fullyProcessed,
        IReadOnlySet<string> verdictWithoutStems,
        IReadOnlySet<string> separatedWithoutKey,
        IReadOnlyDictionary<string, TrackInstruction> instructions,
        IReadOnlyList<QueueItem>? reopened = null,
        string? bumped = null
    )
    {
        var described = items.Select(Described).ToArray();
        var reopenedItems = (reopened ?? []).Select(Described).ToArray();

        // Reopened tracks are described here too. Doing it where they are built instead meant a
        // second list of instruction fields, and the source-URL override was missing from it.
        QueueItem Described(QueueItem item) =>
            instructions.TryGetValue(item.VideoId, out var instruction)
                ? item with
                {
                    Plan = instruction.Plan ?? StemPlan.Default,
                    StatedSourceUrl = instruction.SourceUrl,
                }
                : item;

        return new QueueView
        {
            Items = described,
            FullyProcessed = [.. described.Where(i => fullyProcessed.Contains(i.VideoId))],
            AwaitingSeparation =
            [
                .. described.Where(i =>
                    verdictWithoutStems.Contains(i.VideoId)
                    && !separatedWithoutKey.Contains(i.VideoId)
                ),
                .. reopenedItems,
            ],
            AwaitingKey = [.. described.Where(i => separatedWithoutKey.Contains(i.VideoId))],
            Outstanding =
            [
                .. described.Where(i =>
                    !fullyProcessed.Contains(i.VideoId) && !verdictWithoutStems.Contains(i.VideoId)
                ),
            ],
        }.WithBumpedFirst(bumped);
    }

    /// <summary>
    /// The work lists with the bumped track first, where it is in them. Every other item keeps
    /// its place, so the order stays the playlist's own until the user says otherwise.
    /// </summary>
    /// <remarks>
    /// Applied to the two lists a stage picks from. <see cref="AwaitingKey"/> is not one — nothing
    /// automatic works it, so being first in it would mean nothing (ADR 0015).
    /// </remarks>
    private QueueView WithBumpedFirst(string? bumped) =>
        bumped is not { Length: > 0 } videoId
            ? this
            : this with
            {
                Outstanding = First(Outstanding, videoId),
                AwaitingSeparation = First(AwaitingSeparation, videoId),
            };

    private static IReadOnlyList<QueueItem> First(IReadOnlyList<QueueItem> items, string videoId) =>
        items.FirstOrDefault(i => i.VideoId.Equals(videoId, StringComparison.Ordinal))
            is not { } bumped
            ? items
            : [bumped, .. items.Where(i => !ReferenceEquals(i, bumped))];

    /// <summary>Items whose video is fully processed — the ones deletion would remove.</summary>
    public required IReadOnlyList<QueueItem> FullyProcessed { get; init; }

    /// <summary>
    /// Items with no verdict yet: the actual work list, oldest-added first.
    /// </summary>
    public required IReadOnlyList<QueueItem> Outstanding { get; init; }

    /// <summary>
    /// The next <paramref name="size"/> items to work, plus anything explicitly pinned.
    /// </summary>
    /// <remarks>
    /// Pinned items are included on top of the size, never instead of, so pinning one track
    /// never drops another off the end.
    /// </remarks>
    public IReadOnlyList<QueueItem> NextBatch(int size, ISet<string>? pinned = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        var picked = Outstanding.Take(size).ToList();
        var taken = picked
            .Select(i => i.PlaylistItemId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        picked.AddRange(
            Outstanding.Where(i =>
                i.PlaylistItemId is { } id && pinned?.Contains(id) is true && !taken.Contains(id)
            )
        );

        return picked;
    }

    /// <summary>
    /// Items with a verdict and no stems on disk: the separation work list, oldest-added first.
    /// </summary>
    public required IReadOnlyList<QueueItem> AwaitingSeparation { get; init; }

    /// <summary>
    /// Separated already, and parked for want of a key worth tagging with.
    /// </summary>
    /// <remarks>
    /// Nothing crateside runs moves these on — the way out is a better verdict, a human
    /// decision, so this is a resting place with no urgency.
    /// </remarks>
    public required IReadOnlyList<QueueItem> AwaitingKey { get; init; }
}
