using System.Globalization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Playlist;
using Humanizer;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>The text a judge session is given: its instructions, and the work order rendered for each track.</summary>
public sealed class JudgePrompt(WorkingSet workingSet, IOptions<JudgeOptions> options)
{
    internal const string NoAudioLine =
        "crateside holds no audio for this track, so it cannot detect over windows. Do not ask "
        + "for windows or withhold waiting for them: judge from the whole-track evidence.";

    private readonly JudgeOptions _options = options.Value;

    /// <summary>The judge's instructions, the same for every track (judge-prompt.md).</summary>
    public static string Instructions { get; } = ReadInstructions();

    /// <summary>The work order, rendered for the session; <paramref name="held"/> is the run already recorded for the track.</summary>
    internal string Build(
        WorkOrder order,
        string verdictPath,
        IReadOnlyList<SiteLookup> lookups,
        LedgerRun? held,
        JudgeDirection? direction,
        IReadOnlyList<DetectedWindow> given,
        string? audio
    )
    {
        List<string> lines =
        [
            "Judge this track.",
            string.Empty,
            $"- title: {order.Title}",
            $"- source_url: {order.SourceUrl}",
            $"- evidence: {workingSet.EvidencePathFor(order.TrackKey)}",
            $"- verdict file: {verdictPath}",
        ];

        if (held is { Artist: { } artist, Title: { } title })
            lines.Add(
                $"- already judged as artist \"{artist}\", title \"{title}\": write those again"
            );

        lines.AddRange([string.Empty, EarLine(_options.Ear)]);

        if (audio is null)
            lines.AddRange([string.Empty, NoAudioLine]);

        lines.AddRange(PagesLines(lookups));

        if (workingSet.TryReadEvidence(order.TrackKey, out var evidence))
            lines.AddRange([
                string.Empty,
                "The evidence:",
                string.Empty,
                "```json",
                evidence.TrimEnd(),
                "```",
            ]);

        if (direction is not null)
            lines.AddRange(DirectionLines(direction, given));

        return string.Join('\n', lines);
    }

    /// <summary>The user's windows, note and ear, with each window's evidence; the windows become the sections.</summary>
    private static IEnumerable<string> DirectionLines(
        JudgeDirection direction,
        IReadOnlyList<DetectedWindow> given
    )
    {
        yield return string.Empty;

        yield return direction.Windows is []
            ? "The maintainer asked for this track to be judged again, over the whole track."
            : "The maintainer asked for this judgment and named the stretches where they hear the "
                + "key clearly. crateside detected over each. A window is weaker evidence than the "
                + "whole track (see Windows in your instructions), so weigh each against the whole "
                + "file, not above it. Write one section per window, labeled with the window's "
                + "name, with the key you hear in it.";

        if (direction.Note is { Length: > 0 } note)
        {
            yield return string.Empty;
            yield return $"Their note to you: \"{note}\"";
        }

        if (direction.Ear is { } ear)
        {
            yield return string.Empty;
            yield return $"What they hear: {ear.Name}. crateside records it in `sources` with "
                + "kind `ear`; do not add it yourself.";
        }

        foreach (var line in WindowLines(given))
            yield return line;
    }

    /// <summary>The fetched pages, for the judge to accept or reject as this recording.</summary>
    internal static IEnumerable<string> PagesLines(IReadOnlyList<SiteLookup> lookups)
    {
        if (lookups is [])
            yield break;

        yield return string.Empty;
        yield return "Pages crateside found by artist and title before this session, read for you. "
            + "Whether each is this recording is yours to decide; one you rely on goes in `sources` "
            + "like any other.";
        yield return string.Empty;

        foreach (var page in lookups.SelectMany(lookup => lookup.Pages))
        {
            var about = string.Join(
                ", ",
                new[]
                {
                    page.Arrangement,
                    page.Note,
                    page.Chosen is { } chosen ? ChosenNote(chosen) : null,
                }.OfType<string>()
            );
            var keys = page.Keys is []
                ? "no key stated"
                : string.Join(
                    "; ",
                    page.Keys.Select(key =>
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{(key.Section is { } section ? $"{section}: " : string.Empty)}{key.Key}{(key.Tempo is { } tempo ? $", {tempo} BPM" : string.Empty)}"
                        )
                    )
                );

            yield return $"- {page.Site}, {page.Url}{(about is "" ? string.Empty : $" ({about})")}: "
                + $"\"{page.Heading}\". {keys}";

            if (page.Tab is { } tab)
            {
                foreach (var line in TabLines(tab))
                    yield return $"  - {line}";
            }
        }

        foreach (var page in lookups.SelectMany(lookup => lookup.Rejected ?? []))
            yield return $"- the person says this is not this song, so it is left out: {page.Site}, {page.Url}, \"{page.Heading}\"";

        foreach (var page in lookups.SelectMany(lookup => lookup.Candidates ?? []))
            yield return $"- unconfirmed, not evidence: {page.Site}, {page.Url}, \"{page.Heading}\". "
                + $"{page.Note}. Its keys are not a source for a verdict.";

        foreach (var lookup in lookups.Where(lookup => lookup.Status is not LookupStatus.Found))
            yield return lookup.Status is LookupStatus.NotFound
                ? $"- {lookup.Site}: nothing found"
                : $"- {lookup.Site}: not reached ({lookup.Error})";
    }

    private static string ChosenNote(PageChoiceKind kind) =>
        kind is PageChoiceKind.Added
            ? "the person added this page: it is this song"
            : "the person says this page is this song";

    /// <summary>What a TheoryTab states beyond its sections' first keys: who made it, how much of the recording it maps, and where a section modulates.</summary>
    private static IEnumerable<string> TabLines(HooktheoryTab tab)
    {
        var sections = string.Join(", ", tab.Sections.Select(section => section.Name));
        var wholeSong = tab.AllKeys is { Length: > 0 } keys
            ? $"; the site lists the whole song's keys as {keys}"
            : string.Empty;

        yield return tab.Contributors is []
            ? $"no contributor named; {"section".ToQuantity(tab.Sections.Count)} ({sections}){wholeSong}"
            : $"{"contributor".ToQuantity(tab.Contributors.Count)} ({string.Join(", ", tab.Contributors)}); "
                + $"{"section".ToQuantity(tab.Sections.Count)} ({sections}){wholeSong}";

        if (tab.IsThin)
            yield return "THIN PAGE: one contributor and one or two sections";

        var other = tab.Sections.Count(section => section.TrackKey is not null);
        var synced = tab.SyncedTrackKey is { } video
            ? $"synced to YouTube {video}"
            : "not synced to a video";

        yield return (tab.Coverage, tab.Sections.Any(section => section.Keys is not [])) switch
        {
            ({ } coverage, _) => string.Create(
                CultureInfo.InvariantCulture,
                $"{synced}; its sections span {coverage * 100:0.0}% of that video{(other is 0 ? string.Empty : $", and {"section".ToQuantity(other)} are synced to other videos")}"
            ),
            (null, true) => $"{synced}; no section is synced to it, so its coverage is unknown",
            _ => $"{synced}; the sections' own scores could not be read, so their sync is unknown",
        };

        foreach (var section in tab.Sections)
        {
            var filled = (section.PercentChord, section.PercentMelody) switch
            {
                ({ } chords, { } melody) => string.Create(
                    CultureInfo.InvariantCulture,
                    $" (chords {chords:0}% filled in, melody {melody:0}%)"
                ),
                _ => string.Empty,
            };

            if (section is { Keys.Count: > 1, Harmony: [_, ..] })
                yield return $"{section.Name}, its chords by its score, since the page's numerals stay in its first key: "
                    + $"{HarmonyByKey(section)}{filled}";
            else if (section.Progression is { Length: > 0 } progression)
                yield return $"{section.Name}: {progression}{filled}";

            if (section.Keys is { Count: > 1 } changes)
                yield return $"{section.Name} changes key inside the section: "
                    + string.Join(
                        ", then ",
                        changes.Select(change =>
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{change.Key} from beat {change.Beat}"
                            )
                        )
                    );
        }
    }

    /// <summary><c>E major from beat 1: E:maj B:maj | E minor from beat 9: C:maj D:maj E:min</c>, repeats collapsed as the page collapses them.</summary>
    private static string HarmonyByKey(TabSection section)
    {
        var keys = section.Keys.OrderBy(key => key.Beat).ToList();
        List<string> segments = [];

        foreach (var (key, index) in keys.Select((key, index) => (key, index)))
        {
            List<string> labels =
            [
                .. section
                    .Harmony.Where(chord =>
                        (index == 0 || chord.Beat >= key.Beat)
                        && (index == keys.Count - 1 || chord.Beat < keys[index + 1].Beat)
                    )
                    .Select(chord => chord.Label),
            ];

            if (labels is not [])
                segments.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{key.Key} from beat {key.Beat}: {string.Join(' ', Collapsed(labels))}"
                    )
                );
        }

        return string.Join(" | ", segments);
    }

    private static IEnumerable<string> Collapsed(List<string> labels) =>
        labels.Where((label, index) => index == 0 || labels[index - 1] != label);

    internal static string EarLine(EarWeight ear) =>
        ear switch
        {
            EarWeight.HumanSource =>
                "The maintainer's own reading of the key, where given (crateside records it with kind `ear`), "
                    + "counts like an authoritative source: a dissent from it caps the verdict at "
                    + "MEDIUM, and agreement with a backed keydetect can reach HIGH.",
            _ =>
                "The maintainer's own reading of the key, where given (crateside records it with kind `ear`), "
                    + "is a hint: weigh it, but it never raises confidence by itself.",
        };

    internal static string ForWindows(IReadOnlyList<DetectedWindow> detected, string verdictPath)
    {
        List<string> lines =
        [
            $"crateside detected over the windows you asked for. Judge again with this evidence and write the verdict file at {verdictPath}.",
            .. WindowLines(detected),
        ];

        return string.Join('\n', lines);
    }

    private static IEnumerable<string> WindowLines(IReadOnlyList<DetectedWindow> detected)
    {
        foreach (var (window, evidence, error) in detected)
        {
            yield return string.Empty;
            yield return $"Window {Describe(window)}:";
            yield return evidence is { } json
                ? $"```json\n{json.TrimEnd()}\n```"
                : $"detection failed: {error}";
        }
    }

    private static string Describe(RunWindow window) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"\"{window.Name}\", {window.Start:0.##}–{window.End:0.##} s"
        );

    private static string ReadInstructions()
    {
        using var stream = typeof(VerdictRunner).Assembly.GetManifestResourceStream(
            "judge-prompt.md"
        )!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
