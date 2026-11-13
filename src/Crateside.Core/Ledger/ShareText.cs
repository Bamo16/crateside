using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crateside.Core.Tempo;
using Humanizer;

namespace Crateside.Core.Ledger;

/// <summary>What the share card and the copied text head themselves with, spelled as the page spells it.</summary>
public sealed record ShareSubject(
    string Name,
    string? VideoId,
    string Key,
    string Confidence,
    string? Bpm
)
{
    public string? Link => VideoId is null ? null : $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>A verdict's evidence as Markdown for Discord or an agent: headings, bullets and bold, no tables (#152).</summary>
public static partial class ShareText
{
    private const double FlickerSeconds = 2;

    /// <summary>The paragraphs of <paramref name="text"/> up to a budget of characters, cut at a paragraph and ended with an ellipsis.</summary>
    public static IReadOnlyList<ProseParagraph> Excerpt(string? text, int budget)
    {
        var paragraphs = JudgeProse.Paragraphs(text);
        var kept = 0;
        var length = 0;

        while (
            kept < paragraphs.Count
            && (kept is 0 || length + paragraphs[kept].ToString().Length <= budget)
        )
            length += paragraphs[kept++].ToString().Length;

        return kept == paragraphs.Count
            ? paragraphs
            : [.. paragraphs.Take(kept), new ProseParagraph([new ProseSpan("…")])];
    }

    /// <summary>A file name for the saved card: the song, without what a file system refuses.</summary>
    public static string FileName(string name) =>
        $"{UnsafeFileNameCharacters.Replace(name, string.Empty).Trim()} - key verdict.png";

    public static string Markdown(ShareSubject subject, RunEvidence run)
    {
        var text = new StringBuilder();

        text.AppendLine($"# {subject.Name}");

        if (subject.VideoId is { } videoId)
            text.AppendLine($"Video `{videoId}` · {subject.Link}");

        text.AppendLine().AppendLine("## Verdict").AppendLine();
        text.AppendLine($"- **Key:** {subject.Key}");
        text.AppendLine($"- **Confidence:** {subject.Confidence}");

        if (subject.Bpm is { } bpm)
            text.AppendLine($"- **BPM:** {bpm}");

        if (run.Mixed)
            text.AppendLine(
                "- **Mixed:** major and minor over the same harmony; tagged by the harmony"
            );

        text.AppendLine($"- {RunSummary.Of(run)}");

        if (run.Sections.Count > 1)
            foreach (var section in run.Sections)
                text.AppendLine(
                    $"- **{Plain(section.Label)}:** {ModeText.Named(section.Center)}{SectionNote(run, section)}"
                );

        Paragraphs(text, "Why this key", run.Reasoning);
        Paragraphs(text, "Tonic check", run.TonicCheck);

        if (run.Candidates.Count > 0)
        {
            text.AppendLine().AppendLine("## Considered").AppendLine();

            foreach (var candidate in run.Candidates)
                text.AppendLine($"- **{Plain(candidate.Key)}:** {Plain(candidate.Why)}");
        }

        Detectors(text, run);
        Sources(text, run);

        if (run.Chords.Count > 0)
            text.AppendLine()
                .AppendLine("## Chord progression")
                .AppendLine()
                .AppendLine(string.Join(" · ", Progression.Fold(run.Chords).Select(Folded)));

        if (Flicker(run.KeyEvents) is { } flicker)
            text.AppendLine().AppendLine(flicker);

        return $"{text.ToString().TrimEnd().ReplaceLineEndings("\n")}\n";
    }

    private static void Detectors(StringBuilder text, RunEvidence run)
    {
        if (run.Algorithms.Count > 0)
        {
            text.AppendLine().AppendLine("## What the detectors reported").AppendLine();

            foreach (var algorithm in run.Algorithms)
                text.AppendLine(Reading(algorithm));

            if (Backing(run.Backers) is { } backing)
                text.AppendLine($"- {backing}");
        }

        foreach (var reading in run.Readings.Where(reading => reading.Algorithms.Count > 0))
        {
            text.AppendLine().AppendLine($"### {Span(reading.Window)}").AppendLine();

            foreach (var algorithm in reading.Algorithms)
                text.AppendLine(Reading(algorithm));

            if (Backing(reading.Backers) is { } backing)
                text.AppendLine($"- {backing}");
        }
    }

    private static void Sources(StringBuilder text, RunEvidence run)
    {
        if (run.Sources.Count == 0)
            return;

        text.AppendLine().AppendLine("## Sources").AppendLine();

        foreach (var source in run.Sources.OrderByDescending(source => source.Kind))
        {
            var said = source.StatesKey is { Length: > 0 } stated
                ? ModeText.Stated(stated, source.StatesMode)
                : source.States;
            string?[] parts =
            [
                $"**{source.Host ?? source.Via ?? "no page"}** ({source.Kind.ToString().ToLowerInvariant()})",
                said is { Length: > 0 } ? Plain(said) : null,
                source.Covers,
                source.Relation is { } relation ? Plain(relation) : null,
                source.Note is { Length: > 0 } note ? Plain(note) : null,
                source.IsRemoved ? "removed by the maintainer, not counted" : null,
                source.Link?.ToString(),
            ];

            text.AppendLine($"- {string.Join(" · ", parts.OfType<string>())}");
        }
    }

    private static string Reading(AlgorithmResult algorithm) =>
        algorithm switch
        {
            { Error: { Length: > 0 } error } => $"- **{algorithm.Label}:** {error}",
            { Key: { Length: > 0 } key } =>
                $"- **{algorithm.Label}:** {Plain(key)}{Detail(algorithm)}",
            _ => $"- **{algorithm.Label}:** no key{Detail(algorithm)}",
        };

    private static string Detail(AlgorithmResult algorithm)
    {
        string?[] parts =
        [
            algorithm.Confidence is { } r ? $"r{Figure(r)}" : null,
            algorithm.FlagChip is { } chip ? chip.Text : null,
            algorithm.RunnerUpKey is { } runnerUp
                ? $"runner-up {Plain(runnerUp)}{Figure(algorithm.RunnerUpRatio ?? algorithm.RunnerUpConfidence)}"
                : null,
            algorithm.Reranked is true ? "the chord rerank changed it" : null,
        ];

        return parts.OfType<string>().ToList() is [_, ..] shown
            ? $" ({string.Join(", ", shown)})"
            : string.Empty;
    }

    private static string Figure(double? value) =>
        value is { } shown
            ? $" {shown.ToString("F3", CultureInfo.InvariantCulture)}"
            : string.Empty;

    public static string? Backing(IReadOnlyList<string>? backers) =>
        backers is null
            ? null
            : $"keydetect is backed by {backers switch
            {
                [] => "neither madmom nor Essentia",
                [var one] => $"{AlgorithmResult.LabelOf(one)} only",
                _ => "both madmom and Essentia",
            }}";

    private static string SectionNote(RunEvidence run, Section section) =>
        section.Center == run.Center ? " (primary)"
        : section.IsHypothesis ? " (possible key change, unconfirmed)"
        : string.Empty;

    private static string Span(RunWindow window) =>
        (window.Label is { Length: > 0 } label ? $"{Plain(label)}, " : string.Empty)
        + (
            window.Bars is { } bars
                ? $"bars {bars.Text}"
                : $"{TempoFormat.Clock(window.Start)}–{TempoFormat.Clock(window.End)}"
        );

    private static string Folded(ChordSegment segment) =>
        $"{string.Join(" → ", segment.Chords.Select(JudgeProse.Chord))}{(segment.IsLoop ? $" ×{segment.Repeats}" : null)}";

    public static string? Flicker(IReadOnlyList<KeyEvent> events)
    {
        if (events.Count == 0)
            return null;

        var quick = events
            .Zip(events.Skip(1), (change, next) => next.Seconds - change.Seconds)
            .Count(gap => gap < FlickerSeconds);

        return quick > 0
            ? $"The running key estimate changed {"time".ToQuantity(events.Count)}, {quick} of them within {FlickerSeconds:0} s."
            : $"The running key estimate changed {"time".ToQuantity(events.Count)}.";
    }

    private static void Paragraphs(StringBuilder text, string heading, string? source)
    {
        if (JudgeProse.Paragraphs(source) is not [_, ..] paragraphs)
            return;

        text.AppendLine().AppendLine($"## {heading}").AppendLine();

        text.AppendLine(string.Join(Environment.NewLine + Environment.NewLine, paragraphs));
    }

    private static string Plain(string? text) => JudgeProse.Inline(text).ToString();

    [GeneratedRegex(@"[\\/:*?""<>|\p{Cc}]")]
    private static partial Regex UnsafeFileNameCharacters { get; }
}
