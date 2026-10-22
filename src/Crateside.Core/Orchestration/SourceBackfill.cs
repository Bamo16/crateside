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
            .VideoIds.Where(videoId => workingSet.TryGetSource(videoId, out _))
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. processed
                .StemsByVideoId.Keys.Where(videoId =>
                    !held.Contains(videoId)
                    && processed
                        .StemsFor(videoId)
                        .Any(stem =>
                            stem.Root.Equals(
                                paths.LibraryFolder,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    && !processed.StemsFor(videoId).Any(stem => stem.IsVerbatimSource)
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Downloads each in turn, stopping when cancelled; <paramref name="onEach"/> hears every result.</summary>
    public async Task<IReadOnlyList<BackfillResult>> RunAsync(
        IReadOnlyList<string> videoIds,
        Action<BackfillResult>? onEach = null,
        CancellationToken ct = default
    )
    {
        List<BackfillResult> results = [];

        foreach (var videoId in videoIds)
        {
            if (ct.IsCancellationRequested)
                break;

            // First, so the janitor reads the folder as joined to the store and not as a judging run's.
            workingSet.Prepare(videoId);
            workingSet.RecordLeftStaging(videoId, time.GetUtcNow());

            var download = await downloads.RunAsync(
                $"https://www.youtube.com/watch?v={videoId}",
                workingSet.Prepare(videoId),
                ct: ct
            );

            if (download is { IsSuccess: true, Acquisition: { } acquisition })
                workingSet.SaveAcquisition(videoId, acquisition);
            else
                workingSet.Discard(videoId);

            var result = new BackfillResult(videoId, download.Error);

            results.Add(result);
            onEach?.Invoke(result);
        }

        return results;
    }
}

/// <param name="Error">Null when the source was downloaded.</param>
public sealed record BackfillResult(string VideoId, string? Error);
