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
    /// When the item was added to the playlist.
    /// </summary>
    /// <remarks>
    /// This, not <see cref="Position"/>, is the queue order: position is relative to a
    /// per-playlist setting and can mean opposite things across playlists.
    /// </remarks>
    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>Where the item sits in the playlist. Display only; see <see cref="AddedAt"/>.</summary>
    public int? Position { get; init; }

    /// <summary>Whether this is likely to download at Premium quality.</summary>
    public PremiumOutlook PremiumOutlook { get; init; }

    /// <summary>
    /// True when the video is no longer playable, deleted or made private upstream.
    /// </summary>
    /// <remarks>
    /// YouTube keeps the playlist entry and blanks its title, so it can never become processed.
    /// </remarks>
    public bool IsUnavailable { get; init; }

    /// <summary>
    /// Which files the user wants made for this track.
    /// </summary>
    /// <remarks>
    /// Carried from the user's standing instruction, never inferred here. Defaults to
    /// <see cref="StemPlan.Default"/>, so a reader never has to handle "not stated".
    /// </remarks>
    public StemPlan Plan { get; init; } = StemPlan.Default;

    /// <summary>
    /// What this track already has on disk, which is what makes a cut additive.
    /// </summary>
    /// <remarks>
    /// Empty for a track nothing has been cut for, which is the same thing as owing the whole
    /// plan — so a reader never has to handle "not yet separated" separately.
    /// </remarks>
    public StemInventory Inventory { get; init; } = StemInventory.Empty;

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
    /// <param name="separatedWithoutKey">
    /// Video IDs parked in the unresolved-key root, waiting on a verdict good enough to tag with.
    /// </param>
    /// <param name="reopened">
    /// Kept out of <see cref="Items"/> and <see cref="Outstanding"/>, so a re-cut cannot cost quota.
    /// </param>
    /// <param name="awaitingCut">
    /// Video IDs whose separation is owed with their files left where they are.
    /// </param>
    public static QueueView Compose(
        IReadOnlyList<QueueItem> items,
        IReadOnlySet<string> fullyProcessed,
        IReadOnlySet<string> verdictWithoutStems,
        IReadOnlySet<string> separatedWithoutKey,
        IReadOnlyDictionary<string, TrackInstruction> instructions,
        IReadOnlyList<QueueItem>? reopened = null,
        string? bumped = null,
        IReadOnlySet<string>? awaitingCut = null,
        IReadOnlyDictionary<string, StemInventory>? inventories = null
    )
    {
        var described = items.Select(Described).ToArray();
        var reopenedItems = (reopened ?? []).Select(Described).ToArray();

        // Reopened tracks are described here too. Doing it where they are built instead meant a
        // second list of instruction fields, and the source-URL override was missing from it.
        QueueItem Described(QueueItem item)
        {
            var instruction = instructions.GetValueOrDefault(item.VideoId);

            return item with
            {
                Plan = instruction?.Plan ?? StemPlan.Default,
                StatedSourceUrl = instruction?.SourceUrl,
                Inventory = inventories?.GetValueOrDefault(item.VideoId) ?? StemInventory.Empty,
            };
        }

        return new QueueView
        {
            Items = described,
            FullyProcessed = [.. described.Where(i => fullyProcessed.Contains(i.VideoId))],
            AwaitingSeparation =
            [
                // Two ways in: nothing of the track is on disk, or the user asked for a cut it
                // has not had. The second is what makes adding to a track possible at all —
                // deleting every file used to be the only way back into this list.
                //
                // Either way the separator must have something to do. A plan whose outstanding
                // half is only its source owes a copy, which the download stage makes, and being
                // in this list would queue it for a GPU it never reaches (ADR 0019).
                .. described.Where(i =>
                    !separatedWithoutKey.Contains(i.VideoId)
                    && (
                        verdictWithoutStems.Contains(i.VideoId)
                        || awaitingCut?.Contains(i.VideoId) is true
                    )
                    && Orchestration.SeparationRecipe.For(i.Plan, i.Inventory).Passes > 0
                ),
                // Filtered, not appended bare: a reopened track owing only a handoff or a tagging
                // needs no GPU, and one whose stems are parked for want of a key must not be in
                // the line at the same time as it is parked (#32).
                //
                // No awaitingCut means the caller has not said which of them owe a cut, not that
                // none do, so they all still count.
                .. reopenedItems.Where(i =>
                    !separatedWithoutKey.Contains(i.VideoId)
                    && (awaitingCut is null || awaitingCut.Contains(i.VideoId))
                    && Orchestration.SeparationRecipe.For(i.Plan, i.Inventory).Passes > 0
                ),
            ],
            Reopened = reopenedItems,
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
    /// Tracks still owing a stage that the playlist no longer holds, whichever stage it is.
    /// </summary>
    /// <remarks>
    /// Keeping a row alive and queueing for the GPU are different jobs. This is the first;
    /// <see cref="AwaitingSeparation"/> is the second, and a track owing only a handoff belongs
    /// in one and not the other (#32).
    /// </remarks>
    public IReadOnlyList<QueueItem> Reopened { get; init; } = [];

    /// <summary>
    /// Every item the read produced, once each — what a stage that works any track reads.
    /// </summary>
    /// <remarks>
    /// A source can be added to a track at any point, including one already finished with, so
    /// the download stage cannot read the outstanding list alone (ADR 0019).
    /// </remarks>
    public IEnumerable<QueueItem> Everything =>
        Items
            .Concat(AwaitingSeparation)
            .Concat(Reopened)
            .Concat(AwaitingKey)
            .DistinctBy(item => item.VideoId, StringComparer.Ordinal);

    /// <summary>
    /// Separated already, and parked for want of a key worth tagging with.
    /// </summary>
    /// <remarks>
    /// Nothing crateside runs moves these on — the way out is a better verdict, a human
    /// decision, so this is a resting place with no urgency.
    /// </remarks>
    public required IReadOnlyList<QueueItem> AwaitingKey { get; init; }
}
