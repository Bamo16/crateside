using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>The nominal tempo and the headline source (ADR 0049).</summary>
public sealed class TrackTempoTests
{
    [Theory]
    [InlineData(128.0000004, 128)]
    [InlineData(119.5000003, 119.5)]
    [InlineData(85.52, 85.52)]
    public void A_warp_on_a_whole_or_half_to_a_millionth_is_the_users_declaration(
        double warped,
        double nominal
    ) => Assert.Equal(nominal, Warped(warped).Value);

    [Fact]
    public void A_snapped_warp_takes_the_grid_number_a_tight_reading_sits_on()
    {
        var tempo = Warped(128.002, Tight(128.001));

        Assert.Equal(128, tempo.Value);
        Assert.Equal(128.002, tempo.Measured);
    }

    [Fact]
    public void A_reading_whose_windows_agree_less_tightly_snaps_nothing() =>
        Assert.Equal(128.002, Warped(128.002, Loose(128.001)).Value);

    [Theory]
    [InlineData(128.02, 128.021)]
    [InlineData(90.008, 90.009)]
    public void A_real_tempo_off_the_grid_is_never_snapped(double warped, double read) =>
        Assert.Equal(warped, Warped(warped, Tight(read)).Value);

    [Fact]
    public void A_typed_warp_stays_where_the_user_put_it_whatever_the_reading_says() =>
        Assert.Equal(100, Warped(100, Tight(99.9817)).Value);

    [Fact]
    public void A_warp_an_octave_from_the_reading_is_not_snapped_to_it() =>
        Assert.Equal(64.001, Warped(64.001, Tight(128.0005)).Value);

    [Theory]
    [InlineData(101.0015, 101)]
    [InlineData(160.0006, 160)]
    [InlineData(90.009, 90.009)]
    public void A_reading_alone_snaps_only_within_three_thousandths(double read, double nominal)
    {
        var tempo = TrackTempo.Of(null, Tight(read), 87)!;

        Assert.Equal((TempoSource.Reading, nominal), (tempo.Source, tempo.Value));
        Assert.True(tempo.IsVouched);
    }

    [Fact]
    public void A_loose_reading_is_not_vouched_for() =>
        Assert.False(TrackTempo.Of(null, Loose(123.03), null)!.IsVouched);

    [Fact]
    public void Keytags_estimate_is_the_headline_only_without_a_warp_or_a_reading()
    {
        var tempo = TrackTempo.Of(null, null, 128)!;

        Assert.Equal(TempoSource.Estimate, tempo.Source);
        Assert.False(tempo.IsVouched);
        Assert.Null(TrackTempo.Of(null, null, null));
    }

    [Theory]
    [InlineData(128.001, new[] { 128.001, 128.001, 128.001, 128.001 }, 128.0)]
    [InlineData(128.02, new[] { 128.02, 128.01, 128.03, 128.02 }, 128.02)]
    [InlineData(126.0, new[] { 120.0, 124.0, 128.0, 132.0 }, null)]
    public void A_starter_warp_is_written_at_the_nominal_tempo_of_a_steady_reading(
        double bpm,
        double[] windows,
        double? starter
    ) =>
        Assert.Equal(
            starter,
            TrackTempo.StarterBpm(new BeatReading { Bpm = bpm, Windows = windows })
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
        double? estimate = null
    ) => TrackTempo.Of(new WarpTempo(bpm, TempoShape.Steady, bpm, bpm, []), reading, estimate)!;

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
