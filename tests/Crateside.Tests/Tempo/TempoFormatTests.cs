using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>How each tempo state reads (handoff 13).</summary>
public sealed class TempoFormatTests
{
    [Theory]
    [InlineData(128, "128", ".00")]
    [InlineData(128.02, "128.02", "")]
    [InlineData(119.5, "119.50", "")]
    [InlineData(85.524, "85.52", "")]
    public void A_vouched_tempo_shows_two_decimals_with_00_dropped(
        double bpm,
        string text,
        string padding
    )
    {
        var shown = TempoFormat.Format(Warped(bpm));

        Assert.Equal((text, padding, false), (shown.Text, shown.Padding, shown.IsApproximate));
        Assert.Equal("from your warp", shown.Note);
        Assert.Null(shown.Flag);
    }

    [Fact]
    public void A_tight_reading_is_vouched_and_says_so()
    {
        var shown = TempoFormat.Format(TrackTempo.Of(null, Tight(174.0004), null)!);

        Assert.Equal(
            ("174", "crateside's reading · steady to the hundredth"),
            (shown.Text, shown.Note)
        );
    }

    [Fact]
    public void A_loose_reading_is_a_whole_number_with_a_tilde()
    {
        var reading = new BeatReading { Bpm = 123.03, Windows = [123.0, 123.03, 123.06, 123.09] };
        var shown = TempoFormat.Format(TrackTempo.Of(null, reading, null)!);

        Assert.Equal(("~123", true), (shown.Text, shown.IsApproximate));
        Assert.Equal("crateside's reading · steady, but not to the hundredth", shown.Note);
    }

    [Fact]
    public void Keytags_estimate_is_approximate_and_drawn_as_an_estimate()
    {
        var shown = TempoFormat.Format(TrackTempo.Of(null, null, 127.8)!);

        Assert.Equal(("~128", true, true), (shown.Text, shown.IsApproximate, shown.IsEstimate));
        Assert.Equal("keytag's estimate · not read yet", shown.Note);
    }

    [Fact]
    public void A_drifting_warp_gives_its_range_rounded_outward_and_its_spread_as_a_whole_percent()
    {
        var warp = new WarpTempo(111.3, TempoShape.Drifting, 107.9, 113.7, [])
        {
            Windows = Timed(110.2, 111.0, 112.4),
        };
        var shown = TempoFormat.Format(TrackTempo.Of(warp, null, null)!);

        Assert.Equal("~111", shown.Text);
        Assert.Equal("drifts 110–113 · 2% · from your warp", shown.Note);
        Assert.Equal(new TempoFlag("drifts", "2%"), shown.Flag);
        Assert.Equal("drifts 110–113 · 2%", shown.Brief);
    }

    [Fact]
    public void A_drifting_reading_says_it_is_the_reading()
    {
        var reading = new BeatReading
        {
            Bpm = 92.1,
            Windows = [91.3, 91.6, 92.4, 93.8, 93.5, 92.2, 91.8, 92.0, 91.4],
        };

        Assert.Equal(
            "drifts 91–94 · 3% · crateside's reading",
            TempoFormat.Format(TrackTempo.Of(null, reading, null)!).Note
        );
    }

    [Fact]
    public void A_drift_under_one_percent_reads_as_less_than_one()
    {
        var warp = new WarpTempo(92.2, TempoShape.Drifting, 90.8, 93.5, [])
        {
            Windows = Timed(92.1, 92.4, 92.3),
        };

        Assert.Equal(
            new TempoFlag("drifts", "<1%"),
            TempoFormat.Format(TrackTempo.Of(warp, null, null)!).Flag
        );
    }

    [Fact]
    public void A_mostly_steady_warp_shows_its_main_tempo_and_how_much_of_the_track_it_holds()
    {
        var warp = new WarpTempo(124, TempoShape.MostlySteady, 110, 124, [new StableTempo(124, 91)])
        {
            AudibleBars = 100,
        };
        var shown = TempoFormat.Format(TrackTempo.Of(warp, null, null)!);

        Assert.Equal(("124", false), (shown.Text, shown.IsApproximate));
        Assert.Equal("steady for 91% · one part changes tempo · from your warp", shown.Note);
        Assert.Equal(new TempoFlag("part changes tempo"), shown.Flag);
    }

    [Fact]
    public void A_warp_that_changes_tempo_names_the_second_longest_tempo()
    {
        var two = Changing(new StableTempo(180, 170), new StableTempo(132.004, 24));
        var three = Changing(
            new StableTempo(180, 170),
            new StableTempo(132, 24),
            new StableTempo(90, 8)
        );

        Assert.Equal(
            "changes tempo · 24 bars at 132 · from your warp",
            TempoFormat.Format(two).Note
        );
        Assert.Equal(
            "changes tempo · 24 bars at 132 · and others · from your warp",
            TempoFormat.Format(three).Note
        );
        Assert.Equal(new TempoFlag("changes tempo"), TempoFormat.Format(two).Flag);
    }

    [Theory]
    [InlineData(new[] { 95.4, 127.6, 100 }, "95–128")]
    [InlineData(new[] { 119.5 }, "119.50")]
    [InlineData(new double[0], null)]
    public void A_range_is_rounded_outward_and_one_tempo_is_written_as_itself(
        double[] bpms,
        string? range
    ) => Assert.Equal(range, TempoFormat.Range(bpms.Select(bpm => Warped(bpm))));

    [Fact]
    public void Summaries_carry_no_marks()
    {
        Assert.Equal("124–127", TempoFormat.Bin(124, 127));
        Assert.Equal("128 ×61", TempoFormat.Peak(128, 61));
    }

    [Fact]
    public void A_strip_tick_gives_its_window_and_a_whole_approximate_tempo() =>
        Assert.Equal("2:30–2:38 · ~112", TempoFormat.Window(new TempoWindow(150, 157.6, 111.6)));

    [Fact]
    public void The_evidence_panel_puts_each_other_sources_relation_into_words()
    {
        var agreeing = Warped(128.002, Tight(128.001), estimate: 128);

        Assert.Equal(
            [
                ("crateside's reading", "128", "agrees", false),
                ("keytag's estimate", "~128", "agrees", false),
            ],
            Lines(agreeing)
        );

        var reading = new BeatReading { Bpm = 123.03, Windows = [123.0, 123.03, 123.06, 123.09] };

        Assert.Equal(
            [
                ("Your warp", "—", "not warped yet", false),
                ("keytag's estimate", "~104", "conflict", true),
            ],
            Lines(TrackTempo.Of(null, reading, 104)!)
        );
        Assert.Equal("two-thirds of it", Lines(TrackTempo.Of(null, Tight(92.1), 61.4)!)[1].Comment);
        Assert.Equal("half of it", Lines(TrackTempo.Of(null, Tight(174), 87)!)[1].Comment);
    }

    [Fact]
    public void Without_a_reading_the_panel_says_it_has_not_been_read() =>
        Assert.Contains(
            ("crateside's reading", "—", "not read yet", false),
            Lines(TrackTempo.Of(null, null, 128)!)
        );

    [Theory]
    [InlineData(119.5, 1, "126.61")]
    [InlineData(128, 0, "128")]
    public void A_shift_of_a_vouched_tempo_stays_vouched(double bpm, int semitones, string text) =>
        Assert.Equal(text, TempoFormat.Format(Warped(bpm).Shifted(semitones)).Text);

    [Fact]
    public void A_drifting_tracks_shifts_are_approximate_and_copy_without_the_tilde()
    {
        var warp = new WarpTempo(111.3, TempoShape.Drifting, 107.9, 113.7, [])
        {
            Windows = Timed(110.2, 112.4),
        };
        var shown = TempoFormat.Format(TrackTempo.Of(warp, null, null)!.Shifted(-1));

        Assert.Equal(("~105", "105"), (shown.Text, shown.Figure));
        Assert.Equal("Copied 105. The tempo drifts, so it's approximate.", shown.Copied);
        Assert.Equal("Copied 126.61", TempoFormat.Format(Warped(119.5).Shifted(1)).Copied);
    }

    private static List<(string Label, string Figure, string Comment, bool IsConflict)> Lines(
        TrackTempo tempo
    ) =>
        [
            .. TempoFormat
                .Others(tempo)
                .Select(line =>
                    (line.Label, line.Figure?.Text ?? "—", line.Comment, line.IsConflict)
                ),
        ];

    private static TrackTempo Changing(params StableTempo[] stable) =>
        TrackTempo.Of(
            new WarpTempo(stable[0].Bpm, TempoShape.MostlySteady, 90, 180, stable),
            null,
            null
        )!;

    private static TrackTempo Warped(
        double bpm,
        BeatReading? reading = null,
        double? estimate = null
    ) => TrackTempo.Of(new WarpTempo(bpm, TempoShape.Steady, bpm, bpm, []), reading, estimate)!;

    private static BeatReading Tight(double bpm) =>
        new() { Bpm = bpm, Windows = [bpm, bpm, bpm, bpm] };

    /// <summary>Warp windows of about eight seconds each, one per tempo given.</summary>
    private static TempoWindow[] Timed(params double[] bpms) =>
        [.. bpms.Select((bpm, index) => new TempoWindow(index * 8, (index + 1) * 8, bpm))];
}
