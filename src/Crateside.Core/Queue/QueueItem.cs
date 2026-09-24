namespace Crateside.Core.Queue;

/// <summary>One entry in the Queue playlist.</summary>
public sealed record QueueItem
{
    /// <summary>
    /// The playlist item's own ID, which deletion targets and one video can have two of. Null once
    /// the playlist no longer holds the track.
    /// </summary>
    public string? PlaylistItemId { get; init; }

    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>The uploading channel, for telling two versions of a track apart.</summary>
    public string? ChannelTitle { get; init; }

    /// <summary>
    /// When the item was added — the queue order, since <see cref="Position"/> can mean opposite
    /// things across playlists.
    /// </summary>
    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>Where the item sits in the playlist. Display only; see <see cref="AddedAt"/>.</summary>
    public int? Position { get; init; }

    /// <summary>Whether this is likely to download at Premium quality.</summary>
    public PremiumOutlook PremiumOutlook { get; init; }

    /// <summary>
    /// True when the video was deleted or made private upstream; YouTube keeps the entry, so it
    /// can never become processed.
    /// </summary>
    public bool IsUnavailable { get; init; }

    /// <summary>
    /// Which files the user wants made for this track, from their standing instruction, or
    /// <see cref="StemPlan.Default"/> when they have not said.
    /// </summary>
    public StemPlan Plan { get; init; } = StemPlan.Default;

    /// <summary>
    /// What this track already has on disk, which is what makes a cut additive. Empty means it
    /// owes the whole plan.
    /// </summary>
    public StemInventory Inventory { get; init; } = StemInventory.Empty;

    /// <summary>
    /// The upload to fetch instead of this entry's own video, or null; <see cref="VideoId"/> stays
    /// the join key.
    /// </summary>
    public string? StatedSourceUrl { get; init; }

    /// <summary>The ledger song the user said this track is not, despite its name (#41).</summary>
    public string? DifferentFrom { get; init; }

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
                DifferentFrom = instruction?.DifferentFrom,
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
    /// The two lists a stage picks from, with the bumped track first and every other item where
    /// it was (ADR 0015).
    /// </summary>
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
    /// The next <paramref name="size"/> items to work, plus anything pinned — on top of the size,
    /// never instead.
    /// </summary>
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
    /// Tracks the playlist no longer holds that still owe some stage — which keeps their rows, not
    /// their place in <see cref="AwaitingSeparation"/>.
    /// </summary>
    public IReadOnlyList<QueueItem> Reopened { get; init; } = [];

    /// <summary>
    /// Every item the read produced, once each — what a stage that works any track reads, since a
    /// source can be added even to a finished one (ADR 0019).
    /// </summary>
    public IEnumerable<QueueItem> Everything =>
        Items
            .Concat(AwaitingSeparation)
            .Concat(Reopened)
            .Concat(AwaitingKey)
            .DistinctBy(item => item.VideoId, StringComparer.Ordinal);

    /// <summary>
    /// Separated already, and parked for want of a key worth tagging with. Nothing automatic moves
    /// these on.
    /// </summary>
    public required IReadOnlyList<QueueItem> AwaitingKey { get; init; }
}
