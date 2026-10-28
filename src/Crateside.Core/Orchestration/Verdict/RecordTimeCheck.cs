using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Pairing;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a verdict must be able to show for itself before it is recorded (ADR 0069).</summary>
internal static class RecordTimeCheck
{
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

    /// <summary>What a HIGH verdict lacks of what judge-prompt.md asks for it, or null.</summary>
    public static string? MissingForHigh(RunRecord run, EarWeight ear)
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

        return missing is [] ? null : string.Join(", ", missing);
    }

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
    private static MusicKey? KeyOf(string? text) =>
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
