using System.Text.RegularExpressions;

namespace Crateside.Tests.Tempo;

/// <summary>Only <c>TempoFormat</c> turns a BPM into text, so a bare figure can be trusted (ADR 0049).</summary>
public sealed partial class PagesFormatNoBpmTests
{
    [Fact]
    public void No_page_or_component_formats_a_bpm_itself()
    {
        var pages = Path.Combine(Repository.Root(), "src", "Crateside");
        List<string> found =
        [
            .. Directory
                .EnumerateFiles(pages, "*.razor", SearchOption.AllDirectories)
                .Where(file =>
                    !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                )
                .SelectMany(file =>
                    File.ReadLines(file)
                        .Select((line, index) => (line, index))
                        .Where(numbered => HandFormatted.IsMatch(numbered.line))
                        .Select(numbered =>
                            $"{Path.GetRelativePath(pages, file)}:{numbered.index + 1}: {numbered.line.Trim()}"
                        )
                ),
        ];

        Assert.Empty(found);
    }

    [Theory]
    [InlineData("""@(row.Bpm is { } bpm ? bpm.ToString("N0") : "—")""")]
    [InlineData("""· @(track.Tempo is { } tempo ? $"{tempo.Bpm:N0} BPM, warped" : "not warped")""")]
    [InlineData(
        """<span class="mono">@song.Tempo!.Bpm.ToString("0.00", CultureInfo.InvariantCulture)</span>"""
    )]
    [InlineData("""<strong>@Math.Round(bpm)</strong>""")]
    [InlineData("""<span class="numeric">@TempoCounts.Whole(read.Warped)</span>""")]
    public void The_check_catches_the_ways_pages_used_to_do_it(string line) =>
        Assert.Matches(HandFormatted, line);

    [Theory]
    [InlineData("""@(bpm?.Text ?? "—")<span class="pad">@bpm?.Padding</span>""")]
    [InlineData(
        """<span class="numeric quiet">@read.Ratio.ToString("0.00", CultureInfo.InvariantCulture)</span>"""
    )]
    [InlineData("""@if (Tempo is { Drifts: true } drifting)""")]
    public void The_check_leaves_formatted_tempos_and_other_numbers_alone(string line) =>
        Assert.DoesNotMatch(HandFormatted, line);

    // A tempo-named value given a format, rounded, or written raw.
    [GeneratedRegex(
        @"
        (bpm|tempo)[\w.?!]*\.ToString\(
        | \{[\w.?!]*(bpm|tempo)[\w.?!]*:[\w.#,]+\}
        | Math\.Round\([^)]*(bpm|tempo)
        | TempoCounts\.Whole\(
        | @[\w.?!]*\.(Bpm|Measured)\b(?![\w(])
        ",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex HandFormatted { get; }
}
