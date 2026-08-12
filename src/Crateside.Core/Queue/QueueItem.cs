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

    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>
    /// True when the video is no longer playable — deleted or made private upstream. YouTube
    /// keeps the playlist entry and blanks its title, so it can never become processed and
    /// would otherwise sit in the queue forever.
    /// </summary>
    public bool IsUnavailable { get; init; }

    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>The Queue as work: what is in the playlist, what is done, what is new.</summary>
public sealed record QueueView
{
    public required IReadOnlyList<QueueItem> Items { get; init; }

    /// <summary>Items whose video is fully processed — the ones deletion would remove.</summary>
    public required IReadOnlyList<QueueItem> FullyProcessed { get; init; }

    /// <summary>Items with no verdict yet: the actual work list.</summary>
    public required IReadOnlyList<QueueItem> Outstanding { get; init; }

    /// <summary>
    /// Items with a verdict but no stems on disk — a separation that died partway, or stems
    /// that were deleted. They stay queued on purpose; this names them so a stall is visible
    /// rather than silent.
    /// </summary>
    public required IReadOnlyList<QueueItem> Stalled { get; init; }
}
