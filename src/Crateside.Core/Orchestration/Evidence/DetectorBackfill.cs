using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>
/// Reads madmom and edma over the source of every judged song whose latest run has no reading from
/// them, and adds the readings to that run's evidence (#139). CPU only; it never records a run, so
/// nothing is judged again and no Claude quota is spent.
/// </summary>
public sealed class DetectorBackfill(
    LedgerStore store,
    SourceIndex sources,
    IDetectorSet detectors,
    CratesidePaths paths
)
{
    /// <summary>One source per song that has a run with evidence lacking madmom or edma, the working set's download before a kept sample.</summary>
    public IReadOnlyList<BackerTarget> Missing()
    {
        var ledger = store.Read();
        var held = store.LatestEvidenceJson();
        var entries = sources.Entries(
            ProcessedTracks.Scan(ledger, [paths.StagingFolder, paths.LibraryFolder]),
            ledger
        );

        return
        [
            .. entries
                .Select(entry => (Entry: entry, Song: ledger.SongFor(entry.VideoId)))
                .Where(found =>
                    found is { Song: { } song }
                    && held.TryGetValue(song, out var json)
                    && LacksReadings(json)
                )
                .DistinctBy(found => found.Song, StringComparer.OrdinalIgnoreCase)
                .Select(found => new BackerTarget(found.Song!, found.Entry.Path))
                .OrderBy(target => target.Song, StringComparer.Ordinal),
        ];
    }

    /// <summary>Reads each in turn, stopping when cancelled; <paramref name="onEach"/> hears every result.</summary>
    public async Task<IReadOnlyList<BackerResult>> RunAsync(
        IReadOnlyList<BackerTarget> targets,
        Action<BackerResult>? onEach = null,
        CancellationToken ct = default
    )
    {
        List<BackerResult> results = [];

        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested)
                break;

            var result = await ReadAsync(target, ct);

            results.Add(result);
            onEach?.Invoke(result);
        }

        store.Export();

        return results;
    }

    private async Task<BackerResult> ReadAsync(BackerTarget target, CancellationToken ct)
    {
        if (!File.Exists(target.AudioPath))
            return new BackerResult(target.Song, $"no audio at {target.AudioPath}");

        var (madmom, edma) = await detectors.ReadBackersAsync(target.AudioPath, null, ct);

        return store.EditLatestEvidence(target.Song, json => Attach(json, madmom, edma))
            ? new BackerResult(
                target.Song,
                (madmom as KeyReading.Failed)?.Error ?? (edma as KeyReading.Failed)?.Error
            )
            : new BackerResult(target.Song, "the song has no run with evidence to attach to");
    }

    /// <summary>The evidence with the two readings added where it has none; every other field is kept as it was.</summary>
    internal static string? Attach(string json, KeyReading madmom, KeyReading edma)
    {
        if (JsonNode.Parse(json) is not JsonObject { } evidence)
            return null;

        var algorithms = evidence["algorithms"] as JsonObject ?? [];

        evidence["algorithms"] = algorithms;
        algorithms["madmom"] ??= Reading(madmom);
        algorithms["essentia_edma"] ??= Reading(edma);

        return evidence.ToJsonString(
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        );
    }

    private static bool LacksReadings(string json) =>
        JsonNode.Parse(json) is JsonObject { } evidence
        && evidence["algorithms"] is JsonObject algorithms
        && (algorithms["madmom"] is null || algorithms["essentia_edma"] is null);

    private static JsonObject Reading(KeyReading reading) =>
        reading switch
        {
            KeyReading.Read(var key, var confidence) => new()
            {
                ["key"] = key.LedgerName,
                ["confidence"] = Math.Round(confidence, 4),
            },
            KeyReading.Failed(var error) => new() { ["error"] = error },
            _ => throw new UnreachableException(),
        };
}

/// <summary>A judged song and the source to read over.</summary>
public sealed record BackerTarget(string Song, string AudioPath);

/// <param name="Error">Null when both readings were attached; a reading that failed is attached as its error.</param>
public sealed record BackerResult(string Song, string? Error);
