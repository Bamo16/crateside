using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Downloads the source of every library track that has none held or kept, through the path
/// crateside downloads with, for the retention to keep (#133). Network and CPU only.
/// </summary>
public sealed class SourceBackfill(
    LedgerStore store,
    WorkingSet workingSet,
    DownloadRunner downloads,
    TimeProvider time,
    CratesidePaths paths
)
{
    /// <summary>GUESSED from 68 downloads at 3.0 GB (#133), not measured on the library.</summary>
    public const long GuessedBytesPerSource = 45_000_000;

    /// <summary>Video IDs of library tracks, judged by the ledger or not, with no source held or kept.</summary>
    public IReadOnlyList<string> Missing()
    {
        var processed = ProcessedTracks.Scan(
            store.Read(),
            [paths.StagingFolder, paths.LibraryFolder]
        );
        var held = workingSet
            .TrackKeys.Where(trackKey => workingSet.TryGetSource(trackKey, out _))
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. processed
                .StemsByTrackKey.Keys.Where(trackKey =>
                    TrackKeys.IsVideoId(trackKey)
                    && !held.Contains(trackKey)
                    && processed
                        .StemsFor(trackKey)
                        .Any(stem =>
                            stem.Root.Equals(
                                paths.LibraryFolder,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    && !processed.StemsFor(trackKey).Any(stem => stem.IsVerbatimSource)
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Downloads each in turn, stopping when canceled; <paramref name="onEach"/> hears every result.</summary>
    public async Task<IReadOnlyList<BackfillResult>> RunAsync(
        IReadOnlyList<string> trackKeys,
        Action<BackfillResult>? onEach = null,
        CancellationToken ct = default
    )
    {
        List<BackfillResult> results = [];

        foreach (var trackKey in trackKeys)
        {
            if (ct.IsCancellationRequested)
                break;

            // First, so the janitor reads the folder as joined to the store and not as a judging run's.
            workingSet.Prepare(trackKey);
            workingSet.RecordLeftStaging(trackKey, time.GetUtcNow());

            var download = await downloads.RunAsync(
                $"https://www.youtube.com/watch?v={trackKey}",
                workingSet.Prepare(trackKey),
                ct: ct
            );

            if (download is { IsSuccess: true, Acquisition: { } acquisition })
                workingSet.SaveAcquisition(trackKey, acquisition);
            else
                workingSet.Discard(trackKey);

            var result = new BackfillResult(trackKey, download.Error);

            results.Add(result);
            onEach?.Invoke(result);
        }

        return results;
    }
}

/// <param name="Error">Null when the source was downloaded.</param>
public sealed record BackfillResult(
    [property: JsonPropertyName("videoId")] string TrackKey,
    string? Error
);
