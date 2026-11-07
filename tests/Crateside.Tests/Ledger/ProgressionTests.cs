using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class ProgressionTests
{
    private static string Folded(params string[] chords) =>
        string.Join(" · ", Progression.Fold(chords));

    private static IEnumerable<string> Unfolded(IEnumerable<ChordSegment> segments) =>
        segments.SelectMany(segment =>
            Enumerable.Repeat(segment.Chords, segment.Repeats).SelectMany(chords => chords)
        );

    [Fact]
    public void A_repeated_loop_is_folded_with_its_count()
    {
        string[] chords =
        [
            .. Enumerable.Repeat<string[]>(["G#m", "E"], 18).SelectMany(pair => pair),
            "Em",
            .. Enumerable.Repeat<string[]>(["G#m", "E"], 12).SelectMany(pair => pair),
        ];

        Assert.Equal("G♯m → E ×18 · Em · G♯m → E ×12", Folded(chords));
    }

    [Fact]
    public void Chords_that_do_not_repeat_stay_in_order_in_one_run() =>
        Assert.Equal("Am → F → C → G", Folded("Am", "F", "C", "G"));

    [Fact]
    public void Two_repeats_are_not_a_loop() =>
        Assert.Equal("Am → E → Am → E", Folded("Am", "E", "Am", "E"));

    [Fact]
    public void Chords_between_loops_form_their_own_run() =>
        Assert.Equal(
            "Am → F ×3 · Dm → Em · C → G ×3",
            Folded("Am", "F", "Am", "F", "Am", "F", "Dm", "Em", "C", "G", "C", "G", "C", "G")
        );

    [Fact]
    public void Folding_loses_no_chord()
    {
        string[] chords = ["A", "B", "B", "B", "C#", "D", "C#", "D", "C#", "D", "E"];

        Assert.Equal(chords.Select(JudgeProse.Chord), Unfolded(Progression.Fold(chords)));
    }

    [Theory]
    [InlineData("DISTRUST (r<0.70: unreliable band)", "r < 0.70")]
    [InlineData("gray zone (0.70-0.75)", "r 0.70–0.75")]
    [InlineData("trust band (>0.75; watch for relative-key swap)", "r > 0.75")]
    public void keydetects_flag_is_a_short_chip_with_the_band_in_its_tooltip(
        string flag,
        string text
    )
    {
        var chip = FlagChip.Of(flag);

        Assert.Equal(text, chip?.Text);
        Assert.Contains("keydetect's r", chip?.Tooltip);
    }

    [Fact]
    public void No_flag_has_no_chip() => Assert.Null(FlagChip.Of(null));
}
