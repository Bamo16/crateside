using Crateside.Core.WorkLists;

namespace Crateside.Core.Playlist;

/// <summary>The playlist as last read from the API, reused for two minutes and shared by every reader.</summary>
public sealed class CachedPlaylist(
    PlaylistReader reader,
    PlaylistSettingsStore settings,
    PlaylistRemover remover,
    TimeProvider time
)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _readGate = new(1, 1);

    private IReadOnlyList<WorkItem>? _cachedItems;
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// When the playlist was last fetched from the API — not when the disk was last scanned — or
    /// null if it never has been.
    /// </summary>
    public DateTimeOffset? ReadAt => _cachedItems is null ? null : _cachedAt;

    /// <summary>
    /// Why the playlist could not be read, or null when the last read worked. A report, not a
    /// failure: the rows are still built from what crateside has already taken on.
    /// </summary>
    public string? Problem { get; private set; }

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

    /// <summary>Forgets the cached playlist, so the next read goes to the API.</summary>
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

    /// <summary>The playlist, oldest-added first, from the API or from the last read.</summary>
    public async Task<IReadOnlyList<WorkItem>> ReadAsync(CancellationToken ct = default)
    {
        await _readGate.WaitAsync(ct);

        try
        {
            if (_cachedItems is { } cached && time.GetUtcNow() - _cachedAt < CacheFor)
                return cached;

            try
            {
                // Oldest-added first: the only ordering that means the same thing regardless of
                // whether the playlist adds new videos to the top.
                _cachedItems =
                [
                    .. (await reader.ListAsync(PlaylistId ?? string.Empty, ct)).OrderBy(item =>
                        item.AddedAt ?? DateTimeOffset.MaxValue
                    ),
                ];
                _cachedAt = time.GetUtcNow();
                Problem = null;

                return _cachedItems;
            }
            catch (PlaylistUnavailableException ex)
            {
                // A playlist that cannot be read means no new arrivals, not no queue (ADR 0021).
                Problem = ex.Message;

                return _cachedItems ?? [];
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// Removes specific items the user picked by hand, whatever the automatic predicate would
    /// claim.
    /// </summary>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(
        IReadOnlyList<WorkItem> items,
        CancellationToken ct = default
    )
    {
        var results = await remover.RemoveAsync(items, ct);

        // The playlist just changed underneath the cache, so holding the old one would show
        // removed tracks as still queued — and a drain loop would pick one.
        Invalidate();

        return results;
    }
}
