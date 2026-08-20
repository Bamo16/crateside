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

    /// <summary>
    /// Sorts the playlist into the work lists each stage reads.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="QueueService"/> because this is the part with the actual rules in
    /// it — which track is ready for which stage, and which are off limits — and it can then be
    /// tested without a queue credential or a disk scan. Pure: it decides nothing about what to
    /// do, only about what each thing currently is.
    /// </remarks>
    /// <param name="fullyProcessed">Video IDs with both a verdict and stems on disk.</param>
    /// <param name="verdictWithoutStems">Video IDs with a verdict and no stems anywhere.</param>
    /// <param name="separatedWithoutKey">
    /// Video IDs whose stems are parked in the unresolved-key root. Separated already, and
    /// waiting on a verdict good enough to tag with rather than on anything crateside will do.
    /// </param>
    /// <param name="instructions">The user's standing instructions, keyed by video ID.</param>
    public static QueueView Compose(
        IReadOnlyList<QueueItem> items,
        IReadOnlySet<string> fullyProcessed,
        IReadOnlySet<string> verdictWithoutStems,
        IReadOnlySet<string> separatedWithoutKey,
        IReadOnlyDictionary<string, TrackInstruction> instructions
    )
    {
        // The instrumental-release fact is carried on the item rather than looked up again by
        // every reader, so composing a work order stays pure — it reads the queue item and the
        // ledger and touches nothing.
        var described = items
            .Select(item =>
                instructions.TryGetValue(item.VideoId, out var instruction)
                    ? item with
                    {
                        IsInstrumentalRelease = instruction.IsInstrumentalRelease,
                    }
                    : item
            )
            .ToArray();

        // Held is subtracted from the work lists rather than checked by their readers: a stage
        // takes the first item out of one of these, so keeping held tracks out of them is what
        // makes it impossible for one to be worked — rather than correct only while every
        // reader remembers to filter.
        var held = instructions
            .Values.Where(t => t.IsHeld)
            .Select(t => t.VideoId)
            .ToHashSet(StringComparer.Ordinal);

        return new QueueView
        {
            Items = described,
            FullyProcessed = [.. described.Where(i => fullyProcessed.Contains(i.VideoId))],
            AwaitingSeparation =
            [
                .. described.Where(i =>
                    verdictWithoutStems.Contains(i.VideoId)
                    && !separatedWithoutKey.Contains(i.VideoId)
                    && !held.Contains(i.VideoId)
                ),
            ],
            AwaitingKey =
            [
                .. described.Where(i =>
                    separatedWithoutKey.Contains(i.VideoId) && !held.Contains(i.VideoId)
                ),
            ],
            Held = [.. described.Where(i => held.Contains(i.VideoId))],
            Outstanding =
            [
                .. described.Where(i =>
                    !fullyProcessed.Contains(i.VideoId)
                    && !verdictWithoutStems.Contains(i.VideoId)
                    && !held.Contains(i.VideoId)
                ),
            ],
        };
    }

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
    /// Items with a verdict and no stems on disk: the separation work list, oldest-added first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This used to mean a stall</strong>, back when one run produced the verdict and
    /// the stems together — a track in this state had died partway. Now that the stages are
    /// ordered it is the ordinary waiting room between them, and it is what
    /// <see cref="Orchestration.SeparationSelector"/> reads. A genuine stall is still in here
    /// (stems deleted, a separation that failed), and is told apart by what crateside recorded
    /// having run rather than by the queue.
    /// </para>
    /// <para>
    /// <strong>Held items are not here</strong>, for the same structural reason they are absent
    /// from <see cref="Outstanding"/>: the guard has to be impossible to forget, not merely
    /// applied everywhere today.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<QueueItem> AwaitingSeparation { get; init; }

    /// <summary>
    /// Separated already, and parked for want of a key worth tagging with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Split out of <see cref="AwaitingSeparation"/> because that list was lying.</strong>
    /// A LOW or WITHHELD verdict routes its stems to the unresolved-key root, and the handoff
    /// stage deliberately leaves them there — staging is where tagging happens and there is
    /// nothing to tag them with. But they have a verdict and no stems in staging, which is
    /// exactly the test for "awaiting stems", so they sat in that list permanently looking like
    /// work the GPU had not got to yet.
    /// </para>
    /// <para>
    /// Nothing crateside runs will move these on. The way out is a better verdict, which is a
    /// human decision — so this is a resting place, not a queue, and it carries no urgency.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<QueueItem> AwaitingKey { get; init; }
}
