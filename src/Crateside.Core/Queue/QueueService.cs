using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// The Queue view: the playlist, diffed against what has already been processed.
/// </summary>
public sealed class QueueService(
    IOptions<CratesidePaths> paths,
    QueueSettingsStore settings,
    YouTubeQueueReader reader,
    QueueAuthorizer authorizer,
    QueueRemover remover
)
{
    private readonly CratesidePaths _paths = paths.Value;

    /// <summary>Whether an OAuth client exists, so deletion is possible at all.</summary>
    public bool CanAuthorize => authorizer.IsConfigured;

    /// <summary>Whether consent is currently held. False is an ordinary state, not an error.</summary>
    public bool IsAuthorized => authorizer.IsConfigured && authorizer.HasStoredToken;

    /// <summary>Runs the interactive consent flow. Only from an explicit user action.</summary>
    public Task ConsentAsync(CancellationToken cancellationToken = default) =>
        authorizer.ConsentAsync(cancellationToken);

    /// <summary>Which channel the stored consent belongs to, or null if unknown.</summary>
    public Task<string?> GetAuthorizedChannelAsync(CancellationToken cancellationToken = default) =>
        authorizer.GetAuthorizedChannelAsync(cancellationToken);

    public Task SignOutAsync() => authorizer.SignOutAsync();

    /// <summary>Whether an API key is available. Without one, nothing can be read.</summary>
    public bool HasCredential => reader.HasCredential;

    /// <summary>The playlist currently being read, or null if none is set yet.</summary>
    public string? PlaylistId => settings.Read().PlaylistId;

    public bool IsConfigured => HasCredential && PlaylistId is { Length: > 0 };

    /// <summary>
    /// Points the Queue at a different playlist. Accepts a bare ID or a pasted URL, and
    /// persists outside the working tree.
    /// </summary>
    public void SetPlaylist(string? playlistIdOrUrl) => settings.SavePlaylistId(playlistIdOrUrl);

    /// <summary>
    /// Tells crateside to leave a track alone — it drops out of the work list until released.
    /// </summary>
    public void Hold(string videoId, string? reason = null) => settings.Hold(videoId, reason);

    /// <summary>Puts a held track back into the work list.</summary>
    public void Release(string videoId) => settings.Release(videoId);

    /// <summary>Why a track is being held, in the user's words, or null if it is not held.</summary>
    public string? HoldReason(string videoId) =>
        settings
            .Read()
            .Held.FirstOrDefault(h => h.VideoId.Equals(videoId, StringComparison.Ordinal))
            ?.Reason;

    /// <summary>
    /// Reads the playlist and classifies every item. Throws
    /// <see cref="QueueUnavailableException"/> if the playlist cannot be read — the caller
    /// must render that as its own state, never as an empty queue.
    /// </summary>
    public async Task<QueueView> ReadAsync(CancellationToken cancellationToken = default)
    {
        // Oldest-added first: the Queue is a queue, and added-at is the only ordering that
        // means the same thing whether or not the playlist adds new videos to the top.
        var items = (await reader.ListAsync(PlaylistId ?? string.Empty, cancellationToken))
            .OrderBy(item => item.AddedAt ?? DateTimeOffset.MaxValue)
            .ToArray();

        // Stems live in staging until the work is done, then move to Mashup Samples — both
        // count, because a track that moved out is still processed.
        var processed = ProcessedTracks.Scan(
            KeyLedger.Read(_paths.Ledger),
            [_paths.SampleStaging, _paths.MashupSamples]
        );

        // Held is subtracted from Outstanding rather than checked by its readers: the drainer
        // takes the first outstanding item, so keeping held tracks out of that collection is
        // what makes it impossible for one to be separated.
        var held = settings.Read().Held.Select(h => h.VideoId).ToHashSet(StringComparer.Ordinal);

        return new QueueView
        {
            Items = items,
            FullyProcessed = [.. items.Where(i => processed.IsFullyProcessed(i.VideoId))],
            Stalled = [.. items.Where(i => processed.VerdictWithoutStems.Contains(i.VideoId))],
            Held = [.. items.Where(i => held.Contains(i.VideoId))],
            Outstanding =
            [
                .. items.Where(i =>
                    !processed.IsFullyProcessed(i.VideoId)
                    && !processed.VerdictWithoutStems.Contains(i.VideoId)
                    && !held.Contains(i.VideoId)
                ),
            ],
        };
    }

    /// <summary>
    /// Removes every fully-processed item from the playlist.
    /// </summary>
    /// <remarks>
    /// <strong>Re-reads and re-tests before deleting.</strong> The caller passes nothing: the
    /// set to remove is derived here, at the moment of removal, rather than taken from
    /// whatever a page happened to render earlier. A stale page must never be able to delete
    /// something that has since stopped qualifying.
    /// </remarks>
    public async Task<IReadOnlyList<RemovalResult>> RemoveProcessedAsync(
        CancellationToken cancellationToken = default
    )
    {
        var view = await ReadAsync(cancellationToken);

        return await remover.RemoveAsync(view.FullyProcessed, cancellationToken);
    }

    /// <summary>
    /// Removes specific items the user picked by hand — the escape hatch for anything the
    /// automatic predicate will never claim, such as a track whose stems were deleted rather
    /// than moved. Deliberately does no qualification check: this is the user's explicit call.
    /// </summary>
    public Task<IReadOnlyList<RemovalResult>> RemoveAsync(
        IReadOnlyList<QueueItem> items,
        CancellationToken cancellationToken = default
    ) => remover.RemoveAsync(items, cancellationToken);
}
