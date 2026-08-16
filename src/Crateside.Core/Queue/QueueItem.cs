namespace Crateside.Core.Queue;

/// <summary>One entry in the Queue playlist.</summary>
public sealed record QueueItem
{
    /// <summary>
    /// The playlist item's own ID — what deletion targets. Distinct from the video ID: the
    /// same video added twice has two playlist items.
    /// </summary>
    public required string PlaylistItemId { get; init; }

    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>The uploading channel, for telling two versions of a track apart.</summary>
    public string? ChannelTitle { get; init; }

    /// <summary>
    /// When the item was added to the playlist. <strong>This, not
    /// <see cref="Position"/>, is the queue order</strong> — "Add new videos to top of
    /// playlist" is a per-playlist setting, so position means the opposite thing depending on
    /// how a given playlist is configured, while added-at always means the same thing.
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
    /// The release has no vocals to begin with, so there is nothing to separate out.
    /// </summary>
    /// <remarks>
    /// Carried from the user's standing instruction rather than inferred here. It is what turns
    /// the recipe from the usual pair plus a warp guide into drums-only with a kept source, and
    /// it reaches keytag's ledger as <c>has_vocals: false</c>. False means <em>not stated</em>,
    /// which is not the same as "it has vocals" — but crateside's policy for not-stated is to
    /// expect the usual pair, so the two behave alike here (ADR 0004).
    /// </remarks>
    public bool IsInstrumentalRelease { get; init; }

    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>The Queue as work: what is in the playlist, what is done, what is new.</summary>
public sealed record QueueView
{
    public required IReadOnlyList<QueueItem> Items { get; init; }

    /// <summary>Items whose video is fully processed — the ones deletion would remove.</summary>
    public required IReadOnlyList<QueueItem> FullyProcessed { get; init; }

    /// <summary>
    /// Items with no verdict yet: the actual work list, oldest-added first.
    /// </summary>
    /// <remarks>
    /// <strong>Held items are not here.</strong> That is the whole guard: the drainer reads this
    /// collection, so a held track cannot reach the GPU by any path, rather than by every reader
    /// remembering to filter. The batch size and <see cref="NextBatch"/> are sized off this too,
    /// so they describe work actually intended.
    /// </remarks>
    public required IReadOnlyList<QueueItem> Outstanding { get; init; }

    /// <summary>
    /// Items the user has told crateside to leave alone — usually the wrong upload of the right
    /// song, noticed late and not worth stopping to fix.
    /// </summary>
    /// <remarks>
    /// These stay in the playlist and are not <see cref="FullyProcessed"/>, so nothing will ever
    /// clean them up automatically. That is correct — a hold is the user's decision to revisit —
    /// but it means they accumulate quietly unless the view keeps them visible.
    /// </remarks>
    public required IReadOnlyList<QueueItem> Held { get; init; }

    /// <summary>
    /// The next <paramref name="size"/> items to work, plus anything explicitly pinned.
    /// </summary>
    /// <remarks>
    /// Oldest-added first, because the Queue is a queue: things should mostly come out in the
    /// order they went in. <paramref name="pinned"/> jumps that order for the case the FIFO
    /// rule cannot express — something just added that the user wants in this batch anyway.
    /// Pinned items are included <em>on top of</em> the size, never instead of, so pinning one
    /// track never silently drops another off the end.
    /// </remarks>
    public IReadOnlyList<QueueItem> NextBatch(int size, ISet<string>? pinned = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        var picked = Outstanding.Take(size).ToList();
        var taken = picked.Select(i => i.PlaylistItemId).ToHashSet(StringComparer.Ordinal);

        picked.AddRange(
            Outstanding.Where(i =>
                pinned?.Contains(i.PlaylistItemId) is true && !taken.Contains(i.PlaylistItemId)
            )
        );

        return picked;
    }

    /// <summary>
    /// Items with a verdict but no stems on disk — a separation that died partway, or stems
    /// that were deleted. They stay queued on purpose; this names them so a stall is visible
    /// rather than silent.
    /// </summary>
    public required IReadOnlyList<QueueItem> Stalled { get; init; }
}
