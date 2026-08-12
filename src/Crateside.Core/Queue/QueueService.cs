using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// The Queue view: the playlist, diffed against what has already been processed.
/// </summary>
public sealed class QueueService(
    IOptions<CratesidePaths> paths,
    IOptions<QueueOptions> queueOptions,
    YouTubeQueueReader reader
)
{
    private readonly CratesidePaths _paths = paths.Value;

    public bool IsConfigured => reader.IsConfigured;

    public string? PlaylistId => queueOptions.Value.PlaylistId;

    /// <summary>
    /// Reads the playlist and classifies every item. Throws
    /// <see cref="QueueUnavailableException"/> if the playlist cannot be read — the caller
    /// must render that as its own state, never as an empty queue.
    /// </summary>
    public async Task<QueueView> ReadAsync(CancellationToken cancellationToken = default)
    {
        var items = await reader.ListAsync(cancellationToken);

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
