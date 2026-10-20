using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>The headline tempo and its source (ADR 0049, 0050).</summary>
public sealed class TrackTempoTests
{
    [Theory]
    [InlineData(128.0000004, "128")]
    [InlineData(119.5000003, "119.50")]
    [InlineData(85.52, "85.52")]
    public void A_warp_with_no_reading_is_shown_as_warped(double warped, string shown) =>
        Assert.Equal(shown, TempoFormat.Format(Warped(warped)).Text);

    [Fact]
    public void A_warp_shows_as_itself_whatever_round_number_a_reading_sits_on()
    {
        var tempo = Warped(128.002, Tight(128.001));

        Assert.Equal(128.002, tempo.Value);
        Assert.Equal(128.002, tempo.Measured);
    }

    [Theory]
    [InlineData(101.0015)]
    [InlineData(160.0006)]
    [InlineData(90.009)]
    public void A_reading_alone_shows_as_read(double read)
    {
        var tempo = TrackTempo.Of(null, Tight(read), 87)!;

        Assert.Equal((TempoSource.Reading, read), (tempo.Source, tempo.Value));
        Assert.True(tempo.IsVouched);
    }

    [Fact]
    public void A_steady_warp_is_vouched_only_while_its_grid_holds_against_the_audio()
    {
        Assert.True(Warped(128).IsVouched);
        Assert.False(Warped(128, grid: new(null, 127.988, 23)).IsVouched);
        Assert.False(Warped(128, grid: new(TrueUpReason.Steps, null, 0)).IsVouched);
        Assert.False(
            TrackTempo
                .Of(new WarpTempo(128, TempoShape.Steady, 128, 128, []), null, null)!
                .IsVouched
        );
    }

    [Fact]
    public void A_warp_whose_grid_drifts_is_off_its_tempo_by_the_tempo_that_fits()
    {
        var tempo = Warped(128, grid: new(null, 127.988, 23));

        Assert.Equal(new TempoOff(127.988, 23), tempo.Off);
    }

    [Fact]
    public void A_tight_reading_a_hundredth_from_a_warp_that_holds_calls_it_off()
    {
        Assert.Equal(new TempoOff(99.98, null), Warped(100, Tight(99.98)).Off);
        Assert.Null(Warped(100, Tight(99.995)).Off);
        Assert.Null(Warped(100, Loose(99.98)).Off);
        Assert.Null(Warped(100, Tight(50)).Off);
    }

    [Fact]
    public void A_loose_reading_is_not_vouched_for() =>
        Assert.False(TrackTempo.Of(null, Loose(123.03), null)!.IsVouched);

    [Fact]
    public void The_ledgers_estimate_is_the_headline_only_without_a_warp_or_a_reading()
    {
        var tempo = TrackTempo.Of(null, null, 128)!;

        Assert.Equal(TempoSource.Estimate, tempo.Source);
        Assert.False(tempo.IsVouched);
        Assert.Null(TrackTempo.Of(null, null, null));
    }

    [Theory]
    [InlineData(128.001, new[] { 128.001, 128.001, 128.001, 128.001 }, 128.001)]
    [InlineData(128.02, new[] { 128.02, 128.01, 128.03, 128.02 }, 128.02)]
    [InlineData(126.0, new[] { 120.0, 124.0, 128.0, 132.0 }, null)]
    public void A_starter_warp_is_written_at_the_tempo_of_a_steady_reading_as_read(
        double bpm,
        double[] windows,
        double? starter
    ) =>
        Assert.Equal(
            starter,
            TrackTempo.StarterBpm(new BeatReading { Bpm = bpm, Windows = windows })
        );

    [Fact]
    public void A_reading_the_filter_calls_moving_still_gives_a_starter_tempo() =>
        Assert.Equal(
            136,
            TrackTempo.StarterBpm(
                new BeatReading
                {
                    Bpm = 136,
                    TempoWander = 1.831,
                    Segments = [new(0, 60_000, 136), new(60_000, 120_000, 136.4)],
                    Windows = [136, 136, 136],
                }
            )
        );

    [Fact]
    public void A_drifting_readings_range_leaves_out_a_window_read_at_double_tempo()
    {
        var reading = new BeatReading
        {
            Bpm = 92.1,
            Windows = [91.3, 91.6, 92.4, 184.6, 93.5, 92.2, 91.8],
        };

        var tempo = TrackTempo.Of(null, reading, null)!;

        Assert.True(tempo.Drifts);
        Assert.Equal((91.3, 93.5), tempo.Range);
    }

    [Fact]
    public void A_drifting_warps_range_comes_from_its_30_second_windows_not_its_bars()
    {
        var warp = new WarpTempo(129.9, TempoShape.Drifting, 69.5, 136.5, [])
        {
            Windows = Timed(121.2, 128.4, 131.9),
        };

        Assert.Equal((121.2, 131.9), TrackTempo.Of(warp, null, null)!.Range);
    }

    [Fact]
    public void Each_other_source_is_given_as_it_would_read_alone()
    {
        var tempo = Warped(128.002, Tight(128.001), estimate: 64);

        Assert.Equal(TempoSource.Reading, tempo.From(TempoSource.Reading)?.Source);
        Assert.Equal(64, tempo.From(TempoSource.Estimate)?.Value);
        Assert.Same(tempo, tempo.From(TempoSource.Warp));
    }

    private static TrackTempo Warped(
        double bpm,
        BeatReading? reading = null,
        double? estimate = null,
        WarpGrid? grid = null
    ) =>
        TrackTempo.Of(
            new WarpTempo(bpm, TempoShape.Steady, bpm, bpm, [])
            {
                Grid = grid ?? new(TrueUpReason.WithinNoise, null, 0),
            },
            reading,
            estimate
        )!;

    /// <summary>A reading whose windows agree exactly.</summary>
    private static BeatReading Tight(double bpm) =>
        new() { Bpm = bpm, Windows = [bpm, bpm, bpm, bpm] };

    /// <summary>A reading steady enough not to drift, but not good to the hundredth.</summary>
    private static BeatReading Loose(double bpm) =>
        new() { Bpm = bpm, Windows = [bpm, bpm + 0.03, bpm + 0.06, bpm + 0.09] };

    /// <summary>Warp windows of about eight seconds each, one per tempo given.</summary>
    private static TempoWindow[] Timed(params double[] bpms) =>
        [.. bpms.Select((bpm, index) => new TempoWindow(index * 8, (index + 1) * 8, bpm))];
}
