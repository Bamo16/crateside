using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Pairing;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What crateside rules on a key by itself, from the detectors and the pages the lookup found (ADR 0076).</summary>
internal static class VerdictRules
{
    /// <summary>keydetect's correlation from which one backer is enough for a ruling.</summary>
    public const double ConfidentKeydetect = 0.80;

    private const string Keydetect = "keydetect";
    private const string NoPageLine = "no Hooktheory or Musicnotes page states a key.";

    private static readonly (string Property, string Label)[] Detectors =
    [
        ("ffmpeg_keydetect", Keydetect),
        ("madmom", "madmom"),
        ("essentia_edma", "Essentia"),
    ];

    private static readonly string[] CountedSites = ["Hooktheory", "Musicnotes"];

    /// <summary>A ruling when the detectors and the pages settle a key, else false: the track waits for the person.</summary>
    public static bool TryRule(
        string? evidence,
        IReadOnlyList<SiteLookup> lookups,
        [NotNullWhen(true)] out Ruling? ruling
    )
    {
        ruling = null;

        if (Parse(evidence) is not { } root)
            return false;

        var readings = Readings(root);
        var stated = StatedKeys(lookups);

        if (stated is [])
            ruling = lookups.Any(MayHoldAnotherKey) ? null : DetectorsAlone(root, readings);
        else if (Agreed(readings) is { } key && stated.All(page => page.Key == key))
            ruling = BackedByPages(key, readings, stated);

        return ruling is not null;
    }

    private static Ruling? DetectorsAlone(JsonObject root, List<Reading> readings) =>
        readings switch
        {
            [{ Key: var key }, _, _] when readings.All(reading => reading.Key == key) => Rule(
                key,
                KeyConfidence.Medium,
                readings,
                [],
                $"{Named(readings.Select(reading => reading.Detector))} name {key.LedgerName}; {NoPageLine}"
            ),
            [{ Detector: Keydetect, Key: var key }, .. var rest]
                when Correlation(root) is >= ConfidentKeydetect and var correlation
                    && rest.FirstOrDefault(reading => reading.Key == key) is { } backer => Rule(
                key,
                KeyConfidence.Medium,
                readings,
                [],
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Keydetect} (r {correlation:0.00}) and {backer.Detector} name {key.LedgerName}; {NoPageLine}"
                )
            ),
            _ => null,
        };

    private static Ruling BackedByPages(MusicKey key, List<Reading> readings, List<Stated> stated)
    {
        var sites = stated.Select(page => page.Page.Site).Distinct().Select(Possessive);
        var pages = stated.Select(page => page.Page).Distinct().Count();
        var naming = readings
            .Where(reading => reading.Key == key)
            .Select(reading => reading.Detector);

        return Rule(
            key,
            KeyConfidence.High,
            readings,
            [.. stated.GroupBy(page => page.Page).Select(page => SourceOf(key, [.. page]))],
            $"{Named(naming)} name {key.LedgerName}; {Named(sites)} {(pages == 1 ? "page agrees" : "pages agree")}."
        );
    }

    private static Ruling Rule(
        MusicKey key,
        KeyConfidence confidence,
        List<Reading> readings,
        IReadOnlyList<RunSource> sources,
        string reasoning
    ) =>
        new(
            key,
            confidence,
            sources,
            [
                .. readings
                    .Where(reading => reading.Key != key)
                    .GroupBy(reading => reading.Key)
                    .Select(other => new RunCandidate(
                        other.Key.LedgerName,
                        $"{Named(other.Select(reading => reading.Detector))} read {other.Key.LedgerName}."
                    )),
            ],
            reasoning
        );

    private static RunSource SourceOf(MusicKey key, List<Stated> keys) =>
        new()
        {
            Url = keys[0].Page.Url,
            Via = "found by crateside's lookup",
            Kind = IsUserArrangement(keys[0].Page)
                ? SourceKind.Corroborating
                : SourceKind.Authoritative,
            States = string.Join("; ", keys.Select(stated => stated.Raw).Distinct()),
            StatesKey = key.LedgerName,
            StatesMode = ModeOf(keys[0].Raw),
            Note = keys[0].Page.Note,
        };

    private static DiatonicMode? ModeOf(string raw) =>
        LedgerVocabulary.CollapseModalStatement(raw) is null
            ? null
            : LedgerVocabulary.ParseMode(raw);

    private static bool IsUserArrangement(FetchedPage page) =>
        page is { Site: "Musicnotes", Note: { } note }
        && note.StartsWith("user arrangement", StringComparison.OrdinalIgnoreCase);

    private static MusicKey? Agreed(List<Reading> readings) =>
        readings.GroupBy(reading => reading.Key).FirstOrDefault(key => key.Count() >= 2)?.Key;

    private static List<Reading> Readings(JsonObject root)
    {
        List<Reading> readings = [];

        foreach (var (property, label) in Detectors)
        {
            if (
                root["algorithms"]?[property]?["key"] is JsonValue text
                && text.TryGetValue<string>(out var spelled)
                && RecordTimeCheck.KeyOf(spelled) is { } key
            )
                readings.Add(new(label, key));
        }

        return readings;
    }

    private static double? Correlation(JsonObject root) =>
        root["algorithms"]?["ffmpeg_keydetect"]?["r"] is JsonValue value
        && value.TryGetValue<double>(out var correlation)
            ? correlation
            : null;

    /// <summary>One entry per key a counted page states; a key that does not read as one has a null key, so it never agrees.</summary>
    private static List<Stated> StatedKeys(IReadOnlyList<SiteLookup> lookups) =>
        [
            .. lookups
                .Where(lookup => CountedSites.Contains(lookup.Site))
                .SelectMany(lookup => lookup.Pages)
                .SelectMany(page =>
                    page.Keys.Select(pageKey => new Stated(
                        page,
                        pageKey.Key,
                        RecordTimeCheck.KeyOf(pageKey.Key)
                    ))
                ),
        ];

    /// <summary>A counted site that could not be reached may hold the page that disagrees.</summary>
    private static bool MayHoldAnotherKey(SiteLookup lookup) =>
        CountedSites.Contains(lookup.Site) && lookup.Status is LookupStatus.Unreachable;

    private static string Possessive(string site) => site.EndsWith('s') ? $"{site}'" : $"{site}'s";

    private static string Named(IEnumerable<string> names) =>
        names.ToList() switch
        {
            [var only] => only,
            [.. var init, var last] => $"{string.Join(", ", init)} and {last}",
            _ => string.Empty,
        };

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

    private sealed record Reading(string Detector, MusicKey Key);

    private sealed record Stated(FetchedPage Page, string Raw, MusicKey? Key);
}

/// <summary>A key crateside ruled on, and what it stood on.</summary>
internal sealed record Ruling(
    MusicKey Key,
    KeyConfidence Confidence,
    IReadOnlyList<RunSource> Sources,
    IReadOnlyList<RunCandidate> Candidates,
    string Reasoning
)
{
    public TonalCenter Center { get; } = MusicKey.CenterOf(Key);
}
