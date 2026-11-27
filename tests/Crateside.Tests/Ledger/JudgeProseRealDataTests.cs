using System.Text.RegularExpressions;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed partial class JudgeProseRealDataTests
{
    private static readonly Lazy<IReadOnlyList<RunEvidence>> Evidence = new(() =>
        [.. TestLedger.LatestEvidenceReal().Values]
    );

    private static bool Available => TestLedger.HasReal;

    private static IEnumerable<(string Song, string Text)> Texts() =>
        Evidence.Value.SelectMany(run =>
            new[] { run.Reasoning, run.TonicCheck, run.WithheldReasoning }
                .Concat(run.Windows.Select(window => window.Why))
                .OfType<string>()
                .Select(text => (run.Song, text))
        );

    [Fact]
    public void No_verdict_keeps_a_stray_sharp_beside_a_note_or_a_markdown_marker()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        foreach (var (song, text) in Texts())
        {
            foreach (var paragraph in JudgeProse.Paragraphs(text))
            {
                var plain = string.Concat(
                    paragraph
                        .Spans.Where(span => span.Kind is ProseKind.Plain)
                        .Select(span => span.Text)
                );

                Assert.False(StraySharp.IsMatch(plain), $"{song}: {plain}");
                Assert.DoesNotContain('`', plain);
                Assert.DoesNotContain("**", plain, StringComparison.Ordinal);
                Assert.False(EmphasisPair.IsMatch(plain), $"{song}: {plain}");
            }
        }
    }

    [Fact]
    public void No_bare_letter_is_promoted_outside_a_progression()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        foreach (var (song, text) in Texts())
        {
            foreach (var paragraph in JudgeProse.Paragraphs(text))
            {
                var spans = paragraph.Spans;

                for (var i = 0; i < spans.Count; i++)
                {
                    if (spans[i] is not { Kind: ProseKind.Chord, Text: [>= 'A' and <= 'G'] })
                        continue;

                    var before = i > 0 && ArrowEnd.IsMatch(spans[i - 1].Text);
                    var after = i < spans.Count - 1 && ArrowStart.IsMatch(spans[i + 1].Text);

                    Assert.True(before || after, $"{song}: {paragraph}");
                }
            }
        }
    }

    [Fact]
    public void Every_key_span_names_its_mode_and_every_number_survives()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        foreach (var (_, text) in Texts())
        {
            var paragraphs = JudgeProse.Paragraphs(text);

            Assert.All(
                paragraphs
                    .SelectMany(paragraph => paragraph.Spans)
                    .Where(span => span.Kind is ProseKind.Key),
                span => Assert.Matches(@"^[A-G][♯♭]? (natural |harmonic |melodic )?\w+$", span.Text)
            );
            Assert.Equal(
                Digits.Matches(text).Select(match => match.Value),
                Digits
                    .Matches(string.Concat(paragraphs.Select(paragraph => paragraph.ToString())))
                    .Select(match => match.Value)
            );
        }
    }

    [Fact]
    public void Folding_the_real_progressions_loses_no_chord_and_every_flag_becomes_a_chip()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        foreach (var run in Evidence.Value)
        {
            Assert.Equal(
                run.Chords.Select(JudgeProse.Chord),
                Progression
                    .Fold(run.Chords)
                    .SelectMany(segment =>
                        Enumerable
                            .Repeat(segment.Chords, segment.Repeats)
                            .SelectMany(chords => chords)
                    )
            );
            Assert.All(
                run.Algorithms.Where(algorithm => algorithm.Flag is { Length: > 0 }),
                algorithm => Assert.StartsWith("r ", algorithm.FlagChip!.Text)
            );
        }
    }

    [GeneratedRegex(@"[A-Ga-g]#")]
    private static partial Regex StraySharp { get; }

    [GeneratedRegex(@"(?<![\w*])\*(?=\S)[^*\n]+?(?<=\S)\*(?![\w*])")]
    private static partial Regex EmphasisPair { get; }

    [GeneratedRegex(@"^\s*(\([^()\s]{1,6}\))?\s*[→–]")]
    private static partial Regex ArrowStart { get; }

    [GeneratedRegex(@"[→–]\s*$")]
    private static partial Regex ArrowEnd { get; }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits { get; }
}
