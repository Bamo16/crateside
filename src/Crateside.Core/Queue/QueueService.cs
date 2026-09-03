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
    /// <summary>
    /// How long a playlist read is reused before going back to the API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The playlist is a list a human adds to from their phone, so it changes on the order of
    /// minutes at best — while the readers ask constantly: two drain loops on a five-second busy
    /// delay, plus every visit to the Queue tab. Uncached, that is on the order of a thousand
    /// <c>playlistItems.list</c> calls an hour against a 10,000-a-day allowance, to be told the
    /// same thing each time.
    /// </para>
    /// <para>
    /// It is also what made the Queue tab slow: the render blocked on a live API call every
    /// time, so opening a tab to look at a list cost a network round trip.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private readonly CratesidePaths _paths = paths.Value;
    private readonly SemaphoreSlim _readGate = new(1, 1);

    private IReadOnlyList<QueueItem>? _cachedItems;
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// When the playlist was last actually fetched, or null if it never has been.
    /// </summary>
    /// <remarks>
    /// The API read, not the disk scan — this is what "playlist synced 2 min ago" means, and the
    /// two happen on different clocks: a scan runs on every read while the playlist itself is
    /// reused for <see cref="CacheFor"/>.
    /// </remarks>
    public DateTimeOffset? PlaylistReadAt => _cachedItems is null ? null : _cachedAt;

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
    public void SetPlaylist(string? playlistIdOrUrl)
    {
        settings.SavePlaylistId(playlistIdOrUrl);

        // A different playlist entirely, so the cached one is not merely stale but wrong.
        Invalidate();
    }

    /// <summary>
    /// Tells crateside to leave a track alone — it drops out of the work list until released.
    /// </summary>
    public void Hold(string videoId, string? reason = null) => settings.Hold(videoId, reason);

    /// <summary>Puts a held track back into the work list.</summary>
    public void Release(string videoId) => settings.Release(videoId);

    /// <summary>Why a track is being held, in the user's words, or null if it is not held.</summary>
    public string? HoldReason(string videoId) =>
        InstructionFor(videoId) is { IsHeld: true, HoldReason: var reason } ? reason : null;

    /// <summary>
    /// Records whether a track is an <em>instrumental release</em>. The user's statement, and
    /// the only thing that stops crateside asking the separator for a vocal stem of a track
    /// that has no vocals.
    /// </summary>
    public void SetInstrumentalRelease(string videoId, bool isInstrumentalRelease) =>
        settings.SetInstrumentalRelease(videoId, isInstrumentalRelease);

    /// <summary>The standing instruction for a track, or null when there is none.</summary>
    public TrackInstruction? InstructionFor(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(t => t.VideoId.Equals(videoId, StringComparison.Ordinal));

    /// <summary>
    /// Reads the playlist and classifies every item. Throws
    /// <see cref="QueueUnavailableException"/> if the playlist cannot be read — the caller
    /// must render that as its own state, never as an empty queue.
    /// </summary>
    /// <summary>
    /// Forgets the cached playlist, so the next read goes to the API.
    /// </summary>
    /// <remarks>
    /// For the Refresh button and for anything that changes the playlist itself — a deletion, or
    /// pointing at a different playlist. Everything else is content to see a two-minute-old list,
    /// because that is roughly how fast a human adds to one.
    /// </remarks>
    public void Invalidate()
    {
        _readGate.Wait();

        try
        {
            _cachedItems = null;
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// The playlist, from the API or from the last read.
    /// </summary>
    /// <remarks>
    /// Gated so that several readers arriving at once make one call between them rather than
    /// one each — which is the normal case here, with two drain loops and a page all asking
    /// within the same second.
    /// </remarks>
    private async Task<IReadOnlyList<QueueItem>> ItemsAsync(CancellationToken cancellationToken)
    {
        await _readGate.WaitAsync(cancellationToken);

        try
        {
            if (_cachedItems is { } cached && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
                return cached;

            // Oldest-added first: the Queue is a queue, and added-at is the only ordering that
            // means the same thing whether or not the playlist adds new videos to the top.
            _cachedItems =
            [
                .. (await reader.ListAsync(PlaylistId ?? string.Empty, cancellationToken)).OrderBy(
                    item => item.AddedAt ?? DateTimeOffset.MaxValue
                ),
            ];
            _cachedAt = DateTimeOffset.UtcNow;

            return _cachedItems;
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// Reads the queue and keeps everything the read produced, not just the conclusion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Named for what it costs.</strong> The playlist half is cached; the rest is a ledger
    /// read plus two recursive walks that open every audio file under three roots with TagLib, and
    /// that happens on every call. It is the most expensive thing in the app and must not run per
    /// event.
    /// </para>
    /// <para>
    /// The ledger and both scans used to be built here and dropped on the floor, leaving anything
    /// that needed them to do the same walk again. The Tracks page needs all three, so they are
    /// returned rather than recomputed — one pass over the library instead of two.
    /// </para>
    /// </remarks>
    public async Task<QueueScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var items = await ItemsAsync(cancellationToken);

        // Stems live in staging until the work is done, then move to Mashup Samples — both
        // count, because a track that moved out is still processed.
        var ledger = KeyLedger.Read(_paths.Ledger);
        var processed = ProcessedTracks.Scan(ledger, [_paths.SampleStaging, _paths.MashupSamples]);

        // Scanned separately from the two roots above, and deliberately not added to them: a
        // stem parked here is not processed, and counting it as such would take the track out of
        // the queue and let the janitor discard its working set. What it does mean is that the
        // GPU has already done this track, which is the thing "awaiting stems" could not say.
        var parked = ProcessedTracks.Scan(ledger, [_paths.StemsUnresolvedKey]);

        return new QueueScan
        {
            View = QueueView.Compose(
                items,
                processed.FullyProcessed,
                processed.VerdictWithoutStems,
                items
                    .Where(i => parked.StemsFor(i.VideoId).Count > 0)
                    .Select(i => i.VideoId)
                    .ToHashSet(StringComparer.Ordinal),
                settings.Read().Tracks.ToDictionary(t => t.VideoId, StringComparer.Ordinal)
            ),
            Ledger = ledger,
            Processed = processed,
            ReadAt = PlaylistReadAt,
        };
    }

    /// <summary>The queue sorted into work lists — the conclusion of a <see cref="ScanAsync"/>.</summary>
    public async Task<QueueView> ReadAsync(CancellationToken cancellationToken = default) =>
        (await ScanAsync(cancellationToken)).View;

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

        return await RemoveAsync(view.FullyProcessed, cancellationToken);
    }

    /// <summary>
    /// Removes specific items the user picked by hand — the escape hatch for anything the
    /// automatic predicate will never claim, such as a track whose stems were deleted rather
    /// than moved. Deliberately does no qualification check: this is the user's explicit call.
    /// </summary>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(
        IReadOnlyList<QueueItem> items,
        CancellationToken cancellationToken = default
    )
    {
        var results = await remover.RemoveAsync(items, cancellationToken);

        // The playlist just changed underneath the cache, so holding the old one for another two
        // minutes would show removed tracks as still queued -- and a drain loop would pick one.
        Invalidate();

        return results;
    }
}

/// <summary>
/// One reading of the queue: the work lists, and the material they were derived from.
/// </summary>
/// <remarks>
/// Kept together because it was all produced by the same expensive pass and any of it recomputed
/// separately means walking the library again. <see cref="QueueView"/> alone is the answer to a
/// stage's question; the Tracks page asks a wider one and needs the rest.
/// </remarks>
public sealed record QueueScan
{
    public required QueueView View { get; init; }

    /// <summary>The ledger as it stood for this read, so a second reader gets the same verdicts.</summary>
    public required KeyLedger Ledger { get; init; }

    /// <summary>
    /// Every file found under <c>Sample Staging</c> and <c>Mashup Samples</c>, by video ID. Which
    /// root a file turned up in is what says whether the user has finished with it (ADR 0007).
    /// </summary>
    public required ProcessedTracks Processed { get; init; }

    /// <summary>When the playlist itself was last fetched. Null if it never has been.</summary>
    public DateTimeOffset? ReadAt { get; init; }
}
