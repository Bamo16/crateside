using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>
/// Adds the readings a judged song's latest run lacks, then re-reads those keydetect's current build did not
/// make, over its source on disk; no new run, no re-judging (#139, ADR 0066).
/// </summary>
public sealed class DetectorBackfill(
    LedgerStore store,
    SourceIndex sources,
    IDetectorSet detectors,
    CratesidePaths paths,
    TimeProvider clock
)
{
    private const string Reread = "re-read";

    private static readonly string[] Current =
    [
        KeydetectFilter.Name,
        MadmomKeyCnn.Name,
        EdmaKeyEstimator.Name,
    ];

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The evidence stage's first re-read on keydetect 1.2.1 began at 17:21 on 2026-10-24 (ADR 0066); runs recorded before it hold 1.2.1 readings only if they were written in afterwards.</summary>
    private static readonly DateTimeOffset PinAdopted = new(
        2026,
        10,
        24,
        17,
        21,
        0,
        TimeSpan.FromHours(-5)
    );

    private readonly HashSet<string> _failed = [];

    private bool _legacyStamped;

    /// <summary>
    /// The first judged song, by name, that lacks a reading, else whose evidence another build than keydetect's
    /// read, and has a source on disk, the working set's download before a kept sample.
    /// </summary>
    public async Task<BackfillTarget?> NextAsync(CancellationToken ct = default)
    {
        var held = store.LatestEvidenceJson(withEmpty: true);
        var lacking = held.Select(run => (Song: run.Key, Lacks: Lacking(run.Value)))
            .Where(run => run.Lacks.Count > 0)
            .ToDictionary(run => run.Song, run => run.Lacks, StringComparer.OrdinalIgnoreCase);
        var current = await detectors.KeydetectSha256Async(ct);

        if (current is not null && !_legacyStamped)
        {
            _legacyStamped = true;

            if (
                store.EditEveryLatestEvidence(
                    (recordedAt, json) =>
                        recordedAt < PinAdopted
                        && !LateReading.IsStamped(json)
                        && !IsStale(json, current)
                            ? LateReading.Stamped(json, null)
                            : null
                ) > 0
            )
                store.Export();
        }

        var stale = held.Where(run =>
                current is not null && !lacking.ContainsKey(run.Key) && IsStale(run.Value, current)
            )
            .Select(run => run.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (lacking.Count is 0 && stale.Count is 0)
            return null;

        var ledger = store.Read();
        var found = sources
            .Entries(
                ProcessedTracks.Scan(ledger, [paths.StagingFolder, paths.LibraryFolder]),
                ledger
            )
            .Select(entry => (entry.Path, Song: ledger.SongFor(entry.VideoId)))
            .Where(found => found.Song is not null)
            .Select(found => (found.Path, Song: found.Song!))
            .OrderBy(found => found.Song, StringComparer.Ordinal)
            .ToList();

        return found
                .Where(found => lacking.ContainsKey(found.Song))
                .Select(found => new BackfillTarget(
                    found.Song,
                    found.Path,
                    [
                        .. lacking[found.Song]
                            .Where(detector =>
                                !_failed.Contains(FailureKey(found.Song, found.Path, detector))
                            ),
                    ]
                ))
                .FirstOrDefault(target => target.Detectors.Count > 0)
            ?? found
                .Where(found =>
                    stale.Contains(found.Song)
                    && !_failed.Contains(FailureKey(found.Song, found.Path, Reread))
                )
                .Select(found => new BackfillTarget(found.Song, found.Path, Current, Rereads: true))
                .FirstOrDefault();
    }

    /// <summary>Reads what the target lacks and adds it; a reading that fails is remembered and left out.</summary>
    public async Task<BackfillResult> ReadAsync(BackfillTarget target, CancellationToken ct)
    {
        if (target.Rereads)
            return await RereadAsync(target, ct);

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

            if (
                store.EditLatestEvidence(
                    target.Song,
                    json =>
                        Attach(json, part) is { } attached
                            ? LateReading.Stamped(attached, clock.GetUtcNow())
                            : null
                )
            )
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

        return evidence.ToJsonString(Relaxed);
    }

    /// <summary>The evidence with each field <paramref name="fresh"/> holds replaced, each detector on its own; every other field is kept.</summary>
    internal static string? Refreshed(string json, string fresh)
    {
        if (
            JsonNode.Parse(json) is not JsonObject { } evidence
            || JsonNode.Parse(fresh) is not JsonObject { } read
        )
            return null;

        foreach (var (name, value) in read.ToList())
        {
            read.Remove(name);

            if (name is "algorithms" && value is JsonObject added)
            {
                var algorithms = evidence["algorithms"] as JsonObject ?? [];

                evidence["algorithms"] = algorithms;

                foreach (var (detector, reading) in added.ToList())
                {
                    added.Remove(detector);
                    algorithms[detector] = reading;
                }
            }
            else
                evidence[name] = value;
        }

        return evidence.ToJsonString(Relaxed);
    }

    /// <summary>Replaces the latest run's readings with all three read afresh, unless one of them reads no key this time.</summary>
    private async Task<BackfillResult> RereadAsync(BackfillTarget target, CancellationToken ct)
    {
        var reading = await detectors.ReadEvidenceAsync(target.AudioPath, ct);
        var error = reading switch
        {
            EvidenceReading.Failed(var failed) => failed,
            EvidenceReading.Read(var json) when Unread(json) is [] => null,
            EvidenceReading.Read(var json) =>
                $"{string.Join(", ", Unread(json))} read no key, so the earlier readings stay",
            _ => throw new UnreachableException(),
        };

        if (
            error is null
            && reading is EvidenceReading.Read(var fresh)
            && !store.EditLatestEvidence(
                target.Song,
                json =>
                    Refreshed(json, fresh) is { } refreshed
                        ? LateReading.Stamped(refreshed, clock.GetUtcNow())
                        : null
            )
        )
            error = "the song has no run with evidence to refresh";

        if (error is not null)
        {
            _failed.Add(FailureKey(target.Song, target.AudioPath, Reread));

            return new BackfillResult(
                target.Song,
                [],
                new Dictionary<string, string> { [Reread] = error }
            );
        }

        store.Export();

        return new BackfillResult(target.Song, Current, new Dictionary<string, string>());
    }

    private static List<string> Unread(string json) =>
        JsonNode.Parse(json)?["algorithms"] is { } algorithms
            ? [.. Current.Where(detector => algorithms[detector]?["key"] is not JsonValue)]
            : [.. Current];

    private static bool IsStale(string json, string current) =>
        JsonNode.Parse(json)?["versions"]?["ffmpeg_sha256"] is not JsonValue sha
        || !sha.TryGetValue<string>(out var read)
        || !read.Equals(current, StringComparison.OrdinalIgnoreCase);

    private static List<string> Lacking(string json) =>
        JsonNode.Parse(json) is JsonObject { } evidence
            ? [.. Current.Where(detector => evidence["algorithms"]?[detector] is null)]
            : [];

    private static string FailureKey(string song, string audioPath, string detector) =>
        $"{song}|{audioPath}|{File.GetLastWriteTimeUtc(audioPath).Ticks}|{detector}";
}

/// <summary>A judged song, the source to read over, and the detectors to read: those its evidence lacks, or all three when it <paramref name="Rereads"/>.</summary>
public sealed record BackfillTarget(
    string Song,
    string AudioPath,
    IReadOnlyList<string> Detectors,
    bool Rereads = false
);

public sealed record BackfillResult(
    string Song,
    IReadOnlyList<string> Added,
    IReadOnlyDictionary<string, string> Failed
);
