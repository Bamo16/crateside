using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>
/// Adds the readings a judged song's latest run lacks, read over its source on disk; no new run, no re-judging (#139).
/// </summary>
public sealed class DetectorBackfill(
    LedgerStore store,
    SourceIndex sources,
    IDetectorSet detectors,
    CratesidePaths paths
)
{
    private static readonly string[] Current =
    [
        KeydetectFilter.Name,
        MadmomKeyCnn.Name,
        EdmaKeyEstimator.Name,
    ];

    private readonly HashSet<string> _failed = [];

    /// <summary>The first judged song, by name, that lacks a reading and has a source on disk, the working set's download before a kept sample.</summary>
    public BackfillTarget? Next()
    {
        var lacking = store
            .LatestEvidenceJson()
            .Select(held => (Song: held.Key, Lacks: Lacking(held.Value)))
            .Where(held => held.Lacks.Count > 0)
            .ToDictionary(held => held.Song, held => held.Lacks, StringComparer.OrdinalIgnoreCase);

        if (lacking.Count is 0)
            return null;

        var ledger = store.Read();
        var entries = sources.Entries(
            ProcessedTracks.Scan(ledger, [paths.StagingFolder, paths.LibraryFolder]),
            ledger
        );

        return entries
            .Select(entry => (Entry: entry, Song: ledger.SongFor(entry.VideoId)))
            .Where(found => found.Song is not null && lacking.ContainsKey(found.Song))
            .OrderBy(found => found.Song, StringComparer.Ordinal)
            .Select(found => new BackfillTarget(
                found.Song!,
                found.Entry.Path,
                [
                    .. lacking[found.Song!]
                        .Where(detector =>
                            !_failed.Contains(FailureKey(found.Song!, found.Entry.Path, detector))
                        ),
                ]
            ))
            .FirstOrDefault(target => target.Detectors.Count > 0);
    }

    /// <summary>Reads what the target lacks and adds it; a reading that fails is remembered and left out.</summary>
    public async Task<BackfillResult> ReadAsync(BackfillTarget target, CancellationToken ct)
    {
        Dictionary<string, string> failures = [];
        KeyReading? madmom = null;
        KeyReading? edma = null;
        KeydetectReading? keydetect = null;

        if (!File.Exists(target.AudioPath))
            failures = target.Detectors.ToDictionary(detector => detector, _ => "no audio on disk");
        else
        {
            if (
                target.Detectors.Contains(MadmomKeyCnn.Name)
                || target.Detectors.Contains(EdmaKeyEstimator.Name)
            )
                (madmom, edma) = await detectors.ReadBackersAsync(target.AudioPath, null, ct);

            if (target.Detectors.Contains(KeydetectFilter.Name))
                keydetect = await detectors.ReadKeydetectAsync(target.AudioPath, null, ct);
        }

        List<string> added = [];

        Settle(
            KeydetectFilter.Name,
            keydetect is KeydetectReading.Failed(var kError) ? kError : null
        );
        Settle(MadmomKeyCnn.Name, (madmom as KeyReading.Failed)?.Error);
        Settle(EdmaKeyEstimator.Name, (edma as KeyReading.Failed)?.Error);

        if (added.Count > 0)
        {
            var part = DetectorSet.Part(
                added.Contains(KeydetectFilter.Name) ? keydetect : null,
                added.Contains(MadmomKeyCnn.Name) ? madmom : null,
                added.Contains(EdmaKeyEstimator.Name) ? edma : null
            );

            if (store.EditLatestEvidence(target.Song, json => Attach(json, part)))
                store.Export();
            else
                foreach (var detector in added.ToList())
                {
                    failures[detector] = "the song has no run with evidence to attach to";
                    added.Remove(detector);
                }
        }

        foreach (var detector in failures.Keys)
            _failed.Add(FailureKey(target.Song, target.AudioPath, detector));

        return new BackfillResult(target.Song, added, failures);

        void Settle(string detector, string? error)
        {
            if (!target.Detectors.Contains(detector) || failures.ContainsKey(detector))
                return;

            if (error is null)
                added.Add(detector);
            else
                failures[detector] = error;
        }
    }

    /// <summary>The evidence with the readings in <paramref name="part"/> added where it has none; every other field is kept as it was.</summary>
    internal static string? Attach(string json, JsonObject part)
    {
        if (JsonNode.Parse(json) is not JsonObject { } evidence)
            return null;

        foreach (var (name, value) in part.ToList())
        {
            part.Remove(name);

            if (name is "algorithms" && value is JsonObject added)
            {
                var algorithms = evidence["algorithms"] as JsonObject ?? [];

                evidence["algorithms"] = algorithms;

                foreach (var (detector, reading) in added.ToList())
                {
                    added.Remove(detector);
                    algorithms[detector] ??= reading;
                }
            }
            else if (evidence[name] is null or JsonArray { Count: 0 })
                evidence[name] = value;
        }

        return evidence.ToJsonString(
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        );
    }

    private static List<string> Lacking(string json) =>
        JsonNode.Parse(json) is JsonObject { } evidence
            ? [.. Current.Where(detector => evidence["algorithms"]?[detector] is null)]
            : [];

    private static string FailureKey(string song, string audioPath, string detector) =>
        $"{song}|{audioPath}|{File.GetLastWriteTimeUtc(audioPath).Ticks}|{detector}";
}

/// <summary>A judged song, the source to read over, and the detectors its evidence lacks.</summary>
public sealed record BackfillTarget(string Song, string AudioPath, IReadOnlyList<string> Detectors);

public sealed record BackfillResult(
    string Song,
    IReadOnlyList<string> Added,
    IReadOnlyDictionary<string, string> Failed
);
