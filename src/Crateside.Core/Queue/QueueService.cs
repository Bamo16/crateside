using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// The Queue view: the playlist, diffed against what has already been processed.
/// </summary>
public sealed class QueueService(
    IOptions<CratesidePaths> paths,
    QueueSettingsStore settings,
    YouTubeQueueReader reader
)
{
    private readonly CratesidePaths _paths = paths.Value;

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
    /// Reads the playlist and classifies every item. Throws
    /// <see cref="QueueUnavailableException"/> if the playlist cannot be read — the caller
    /// must render that as its own state, never as an empty queue.
    /// </summary>
    public async Task<QueueView> ReadAsync(CancellationToken cancellationToken = default)
    {
        var items = await reader.ListAsync(PlaylistId ?? string.Empty, cancellationToken);

        // Stems live in staging until the work is done, then move to Mashup Samples — both
        // count, because a track that moved out is still processed.
        var processed = ProcessedTracks.Scan(
            KeyLedger.Read(_paths.Ledger),
            [_paths.SampleStaging, _paths.MashupSamples]
        );

        return new QueueView
        {
            Items = items,
            FullyProcessed = [.. items.Where(i => processed.IsFullyProcessed(i.VideoId))],
            Stalled = [.. items.Where(i => processed.VerdictWithoutStems.Contains(i.VideoId))],
            Outstanding =
            [
                .. items.Where(i =>
                    !processed.IsFullyProcessed(i.VideoId)
                    && !processed.VerdictWithoutStems.Contains(i.VideoId)
                ),
            ],
        };
    }
}
