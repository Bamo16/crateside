using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>Video IDs crateside has separation to redo for, whatever the playlist says.</summary>
public delegate IReadOnlySet<string> ReadReopenedTracks();

public sealed class QueueService(
    IOptions<CratesidePaths> paths,
    QueueSettingsStore settings,
    YouTubeQueueReader reader,
    QueueAuthorizer authorizer,
    QueueRemover remover,
    ReadReopenedTracks? reopened = null
)
{
    /// <summary>How long a playlist read is reused before going back to the API.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private readonly CratesidePaths _paths = paths.Value;
    private readonly SemaphoreSlim _readGate = new(1, 1);

    private IReadOnlyList<QueueItem>? _cachedItems;
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// When the playlist was last actually fetched, or null if it never has been.
    /// </summary>
    /// <remarks>
    /// The API read, not the disk scan — the two run on different clocks: a scan happens on
    /// every read while the playlist itself is cached for <see cref="CacheFor"/>.
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

    /// <summary>How often crateside looks at the playlist unprompted.</summary>
    public int RefreshMinutes =>
        settings.Read().RefreshMinutes ?? QueueSettings.DefaultRefreshMinutes;

    /// <summary>Sets it. Zero turns it off.</summary>
    public void SetRefreshMinutes(int minutes) => settings.SaveRefreshMinutes(minutes);

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
    /// Records whether a track is an instrumental release — the user's statement, and the
    /// only thing that stops crateside asking the separator for a vocal stem of a track with
    /// no vocals.
    /// </summary>
    public void SetInstrumentalRelease(string videoId, bool isInstrumentalRelease) =>
        settings.SetInstrumentalRelease(videoId, isInstrumentalRelease);

    /// <summary>The standing instruction for a track, or null when there is none.</summary>
    public TrackInstruction? InstructionFor(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(t => t.VideoId.Equals(videoId, StringComparison.Ordinal));

    /// <summary>Forgets the cached playlist, so the next read goes to the API.</summary>
    /// <remarks>
    /// For the Refresh button and for anything that changes the playlist itself. Everything
    /// else is content to see a two-minute-old list.
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
    /// one each.
    /// </remarks>
    private async Task<IReadOnlyList<QueueItem>> ItemsAsync(CancellationToken cancellationToken)
    {
        await _readGate.WaitAsync(cancellationToken);

        try
        {
            if (_cachedItems is { } cached && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
                return cached;

            // Oldest-added first: the only ordering that means the same thing regardless of
            // whether the playlist adds new videos to the top.
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
    /// The playlist half is cached; the rest is a ledger read plus two recursive walks that
    /// open every audio file under three roots with TagLib — the most expensive thing in the
    /// app, and must not run per event.
    /// </remarks>
    public async Task<QueueScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var items = await ItemsAsync(cancellationToken);

        // Both staging and Mashup Samples count — a track that moved out is still processed.
        var ledger = KeyLedger.Read(_paths.Ledger);
        var processed = ProcessedTracks.Scan(ledger, [_paths.SampleStaging, _paths.MashupSamples]);

        // Deliberately not counted as processed: that would let the janitor discard the
        // working set. It does mean the GPU has already run this track.
        var parked = ProcessedTracks.Scan(ledger, [_paths.StemsUnresolvedKey]);

        var instructions = settings
            .Read()
            .Tracks.ToDictionary(t => t.VideoId, StringComparer.Ordinal);

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
                instructions,
                Reopened(items, ledger)
            ),
            Ledger = ledger,
            Processed = processed,
            Instructions = instructions,
            ReadAt = PlaylistReadAt,
        };
    }

    /// <summary>
    /// Tracks with separation to redo that the playlist no longer holds.
    /// </summary>
    /// <remarks>
    /// Gated to only tracks the ledger has already judged, so these can never reach
    /// <see cref="QueueView.Outstanding"/>. They join <see cref="QueueView.AwaitingSeparation"/>
    /// and nothing else.
    /// </remarks>
    private IReadOnlyList<QueueItem> Reopened(
        IReadOnlyList<QueueItem> items,
        Ledger.KeyLedger ledger
    )
    {
        if (reopened?.Invoke() is not { Count: > 0 } videoIds)
            return [];

        var queued = items.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);
        var instructions = settings.Read().Tracks;

        return
        [
            .. videoIds
                .Where(videoId => !queued.Contains(videoId))
                .Select(videoId =>
                    ledger.TryGetLatestByVideoId(videoId, out var run)
                        ? new QueueItem
                        {
                            VideoId = videoId,
                            Title = run.DisplayName,
                            IsInstrumentalRelease =
                                instructions
                                    .FirstOrDefault(t => t.VideoId == videoId)
                                    ?.IsInstrumentalRelease
                                    is true,
                        }
                        : null
                )
                .OfType<QueueItem>(),
        ];
    }

    /// <summary>
    /// The queue sorted into work lists — the conclusion of a <see cref="ScanAsync"/>.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="QueueUnavailableException"/> if the playlist cannot be read — the
    /// caller must render that as its own state, never as an empty queue.
    /// </remarks>
    public async Task<QueueView> ReadAsync(CancellationToken cancellationToken = default) =>
        (await ScanAsync(cancellationToken)).View;

    /// <summary>
    /// Removes every fully-processed item from the playlist.
    /// </summary>
    /// <remarks>
    /// Re-reads and re-tests before deleting — a stale page must never be able to delete
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

        // The playlist just changed underneath the cache, so holding the old one would show
        // removed tracks as still queued — and a drain loop would pick one.
        Invalidate();

        return results;
    }
}

/// <summary>
/// One reading of the queue: the work lists, and the material they were derived from.
/// </summary>
/// <remarks>Kept together because recomputing any of it separately means walking the library again.</remarks>
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

    /// <summary>
    /// The user's standing instructions, by video ID.
    /// </summary>
    /// <remarks>
    /// Carried whole, not only applied to the work lists — a track leaves the playlist once
    /// processed while its instructions (such as having no vocals) go on being true.
    /// </remarks>
    public required IReadOnlyDictionary<string, TrackInstruction> Instructions { get; init; }

    /// <summary>When the playlist itself was last fetched. Null if it never has been.</summary>
    public DateTimeOffset? ReadAt { get; init; }
}
