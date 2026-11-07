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
        ruling = Decide(evidence, lookups) is RulesOutcome.Ruled(var ruled) ? ruled : null;

        return ruling is not null;
    }

    /// <summary>The ruling, or in one plain line why the rules left the track open.</summary>
    public static RulesOutcome Decide(string? evidence, IReadOnlyList<SiteLookup> lookups)
    {
        if (Parse(evidence) is not { } root || Readings(root) is not [_, ..] readings)
            return new RulesOutcome.Open("The evidence holds no detector readings.");

        var stated = StatedKeys(lookups);
        var ruling = stated switch
        {
            [] when lookups.Any(MayHoldAnotherKey) => null,
            [] => DetectorsAlone(root, readings),
            _ when Agreed(readings) is { } key && stated.All(page => page.Key == key) =>
                BackedByPages(key, readings, stated),
            _ => null,
        };

        return ruling is not null
            ? new RulesOutcome.Ruled(ruling)
            : new RulesOutcome.Open(WhyOpen(root, readings, stated, lookups));
    }

    private static string WhyOpen(
        JsonObject root,
        List<Reading> readings,
        List<Stated> stated,
        IReadOnlyList<SiteLookup> lookups
    )
    {
        if (lookups.FirstOrDefault(MayHoldAnotherKey) is { } unreachable)
            return $"{unreachable.Site} could not be reached, and a page there may name another key, so nothing is ruled without it.";

        var named = readings
            .GroupBy(reading => reading.Key)
            .Select(key =>
                (
                    Key: key.Key,
                    Detectors: key.Select(reading => reading.Detector).ToList(),
                    Pages: stated
                        .Where(page => page.Key == key.Key)
                        .Select(page => page.Page.Site)
                        .Distinct()
                        .ToList()
                )
            )
            .Concat(
                stated
                    .Where(page =>
                        page.Key is { } key && readings.All(reading => reading.Key != key)
                    )
                    .GroupBy(page => page.Key)
                    .Where(group => group.Key is not null)
                    .Select(group =>
                        (
                            Key: group.Key!,
                            Detectors: new List<string>(),
                            Pages: group.Select(page => page.Page.Site).Distinct().ToList()
                        )
                    )
            )
            .OrderByDescending(entry => entry.Detectors.Count)
            .ThenByDescending(entry => entry.Pages.Count)
            .Select(entry =>
                $"{entry.Key.LedgerName} from {Named([.. entry.Detectors, .. entry.Pages])}"
            );

        var tail = stated is []
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"No Hooktheory or Musicnotes page states a key. Without one, all three detectors must name one key, or keydetect and one more with r of at least {ConfidentKeydetect:0.00}{(Correlation(root) is { } r ? $" (here {r:0.00})" : null)}."
            )
            : "Tier A needs two detectors and the pages on one key.";

        return $"{string.Join("; ", named)}. {tail}";
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
            Via = ViaOf(keys[0].Page),
            Kind = IsUserArrangement(keys[0].Page)
                ? SourceKind.Corroborating
                : SourceKind.Authoritative,
            States = string.Join("; ", keys.Select(stated => stated.Raw).Distinct()),
            StatesKey = key.LedgerName,
            StatesMode = ModeOf(keys[0].Raw),
            Note = keys[0].Page.Note,
        };

    internal static string ViaOf(FetchedPage page) =>
        page.Chosen switch
        {
            PageChoiceKind.Promoted => "counted at your say",
            PageChoiceKind.Added => "added by you",
            _ => "found by crateside's lookup",
        };

    private static DiatonicMode? ModeOf(string raw) =>
        LedgerVocabulary.CollapseModalStatement(raw) is null
            ? null
            : LedgerVocabulary.ParseMode(raw);

    internal static bool IsUserArrangement(FetchedPage page) =>
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
    internal static bool MayHoldAnotherKey(SiteLookup lookup) =>
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

/// <summary>What the rules made of a track: a ruling, or the reason they left it open.</summary>
internal abstract record RulesOutcome
{
    private RulesOutcome() { }

    public sealed record Ruled(Ruling Ruling) : RulesOutcome;

    public sealed record Open(string Reason) : RulesOutcome;
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
