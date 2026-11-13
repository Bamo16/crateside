using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Pairing;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a verdict must be able to show for itself before it is recorded (ADR 0069).</summary>
internal static class RecordTimeCheck
{
    private const int BackersNeeded = 2;

    private static readonly string[] KeyProperties = ["key", "key_alt", "recommended"];
    private static readonly string[] Detectors = ["ffmpeg_keydetect", "madmom", "essentia_edma"];

    /// <summary>Why the center's key is nowhere in what the session was given or wrote, or null.</summary>
    public static string? UnfoundKey(RunRecord run, IReadOnlyList<SiteLookup> lookups)
    {
        if (run.Center is not { } center)
            return null;

        var key = MusicKey.Of(center);
        var known = EvidenceKeys(run.Evidence)
            .Concat(lookups.SelectMany(PageKeys))
            .Concat(run.Candidates.Select(candidate => KeyOf(candidate.Key)))
            .Concat(run.Sources.SelectMany(SourceKeys))
            .Concat(run.Sections.Select(section => MusicKey.Of(section.Center)));

        return known.Contains(key)
            ? null
            : $"center: {center.Name} appears nowhere in the evidence, the pages, the sources or "
                + "your candidates. Judge a key the evidence holds, or withhold.";
    }

    /// <summary>Why a primary center that only an unbacked whole-file read holds is refused once windows were judged, or null (ADR 0095).</summary>
    public static string? PrimaryOutsideWindows(RunRecord run, EarWeight ear)
    {
        if (
            run
            is not {
                Confidence: not KeyConfidence.Withheld,
                Center: { } center,
                Windows.Count: > 0
            }
        )
            return null;

        var key = MusicKey.Of(center);

        return
            SectionSupports
                .Readings(run)
                .Any(reading => reading.Algorithms.Any(result => KeyOf(result.Key) == key))
            || run.Sources.Any(source => AgreesAsHuman(source, key, ear))
            || EvidenceDetectors(run.Evidence, key) >= BackersNeeded
            ? null
            : $"center: {center.Name} is no center a window read, no human source states it, and "
                + "only an unbacked whole-file read holds it. Judge a center the windows or a "
                + "source hold, or withhold.";
    }

    /// <summary>Whether a MEDIUM or HIGH verdict leans on sections nothing independent places, its own center resting on nothing else (ADR 0095).</summary>
    public static bool LeansOnHypothesis(RunRecord run, EarWeight ear) =>
        run is { Confidence: KeyConfidence.High or KeyConfidence.Medium, Center: { } center }
        && SectionSupports.Classify(run) is var sections
        && sections.Any(section => section.IsHypothesis)
        && !sections.Any(section => section.IsConfirmed)
        && !IndependentSupport(run, MusicKey.Of(center), ear)
        && EvidenceDetectors(run.Evidence, MusicKey.Of(center)) < BackersNeeded;

    private static bool IndependentSupport(RunRecord run, MusicKey key, EarWeight ear) =>
        SectionSupports
            .Readings(run)
            .Any(reading => SectionSupports.Backed(reading.Algorithms, key))
        || run.Sources.Any(source => AgreesAsHuman(source, key, ear));

    /// <summary>What a HIGH verdict lacks of what judge-prompt.md asks for it, or null; a Hooktheory page the work order marked thin does not dissent.</summary>
    public static string? MissingForHigh(
        RunRecord run,
        EarWeight ear,
        IReadOnlyList<SiteLookup> lookups
    )
    {
        if (run is not { Confidence: KeyConfidence.High, Center: { } center })
            return null;

        var key = MusicKey.Of(center);
        List<string> missing = [];

        if (EvidenceDetectors(run.Evidence, key) < 2)
            missing.Add(
                $"backed detectors (no two of keydetect, madmom and Essentia name {center.Name})"
            );

        if (string.IsNullOrWhiteSpace(run.TonicCheck))
            missing.Add("a tonic check in favor");

        if (!run.Sources.Any(source => AgreesAsHuman(source, key, ear)))
            missing.Add($"a human source that agrees on {center.Name}");

        if (
            run.Sources.FirstOrDefault(source => Dissents(source, key, lookups)) is
            { StatesKey: var stated } dissent
        )
            missing.Add(
                $"agreement from every authoritative source ({dissent.Url ?? dissent.Via} states {stated})"
            );

        return missing is [] ? null : string.Join(", ", missing);
    }

    private static bool Dissents(
        RunSource source,
        MusicKey key,
        IReadOnlyList<SiteLookup> lookups
    ) =>
        source is { Kind: SourceKind.Authoritative }
        && KeyOf(source.StatesKey) is { } stated
        && stated != key
        && !lookups.Any(lookup =>
            lookup.Pages.Any(page => page.Tab is { IsThin: true } && SameUrl(page.Url, source.Url))
        );

    private static bool SameUrl(string page, string? source) =>
        source is not null
        && page.TrimEnd('/').Equals(source.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool AgreesAsHuman(RunSource source, MusicKey key, EarWeight ear) =>
        (
            source.Kind is SourceKind.Authoritative or SourceKind.Corroborating
            || source is { Kind: SourceKind.Ear } && ear is EarWeight.HumanSource
        )
        && KeyOf(source.StatesKey) == key;

    private static int EvidenceDetectors(string? evidence, MusicKey key) =>
        Parse(evidence) is { } root && root["algorithms"] is JsonObject algorithms
            ? Detectors.Count(name => KeyOf(algorithms[name]?["key"]?.GetValue<string>()) == key)
            : 0;

    /// <summary>The keys the detectors read in an evidence report, whole-track and window.</summary>
    internal static IEnumerable<MusicKey> DetectedKeys(string? evidence) =>
        Parse(evidence) is { } root ? Walk(root).OfType<MusicKey>() : [];

    private static IEnumerable<MusicKey?> EvidenceKeys(string? evidence) =>
        Parse(evidence) is { } root ? Walk(root) : [];

    private static IEnumerable<MusicKey?> Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var (name, value) in properties)
                {
                    if (
                        KeyProperties.Contains(name)
                        && value is JsonValue text
                        && text.TryGetValue<string>(out var spelled)
                    )
                        yield return KeyOf(spelled);
                    else
                    {
                        foreach (var inner in Walk(value))
                            yield return inner;
                    }
                }

                break;

            case JsonArray items:
                foreach (var item in items)
                {
                    foreach (var inner in Walk(item))
                        yield return inner;
                }

                break;
        }
    }

    private static IEnumerable<MusicKey?> PageKeys(SiteLookup lookup) =>
        lookup.Pages.SelectMany(page =>
            page.Keys.Select(pageKey => KeyOf(pageKey.Key))
                .Concat(
                    (page.Tab?.Sections ?? []).SelectMany(section =>
                        section.Keys.Select(tabKey => KeyOf(tabKey.Key))
                    )
                )
        );

    private static IEnumerable<MusicKey?> SourceKeys(RunSource source) =>
        [KeyOf(source.StatesKey), KeyOf(source.States)];

    /// <summary>A key in any usual spelling, a mode collapsed to its major or minor as the ledger does.</summary>
    internal static MusicKey? KeyOf(string? text) =>
        MusicKey.FromString(text)
        ?? MusicKey.FromString(
            LedgerVocabulary.NormalizeKey(text) ?? LedgerVocabulary.CollapseModalStatement(text)
        );

    private static JsonObject? Parse(string? json)
    {
        try
        {
            return json is { Length: > 0 } ? JsonNode.Parse(json) as JsonObject : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
