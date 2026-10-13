using AbleKit.Analysis;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

public sealed class TrueUpDecisionTests
{
    private const int SampleRate = SteadyTrack.SampleRate;

    [Fact]
    public void A_warp_a_hair_under_a_whole_tempo_the_track_sits_at_is_retyped_to_it()
    {
        var verdict = TrueUpDecision.Of(
            SteadyTrack.Analysis(warpBpm: 159.995, trackBpm: 160),
            SampleRate
        );

        var retype = Assert.IsType<TrueUpVerdict.Retype>(verdict);
        Assert.True(retype.Whole);
        Assert.Equal(160, retype.Bpm);
        Assert.Equal(0, retype.DriftAfterMs, 1);
    }

    [Fact]
    public void A_steady_tempo_that_is_not_whole_is_found_from_the_drift()
    {
        var verdict = TrueUpDecision.Of(
            SteadyTrack.Analysis(warpBpm: 125.004, trackBpm: 125.026),
            SampleRate
        );

        var retype = Assert.IsType<TrueUpVerdict.Retype>(verdict);
        Assert.False(retype.Whole);
        Assert.Equal(125.026, retype.Bpm, 3);
    }

    [Fact]
    public void A_warp_at_the_tempo_the_track_really_has_is_left_even_beside_a_whole_number()
    {
        var verdict = TrueUpDecision.Of(
            SteadyTrack.Analysis(warpBpm: 94.6199, trackBpm: 94.6199),
            SampleRate
        );

        Assert.Equal(new TrueUpVerdict.Leave(TrueUpReason.WithinNoise), verdict);
    }

    [Fact]
    public void A_warp_that_holds_is_left_alone()
    {
        var verdict = TrueUpDecision.Of(
            SteadyTrack.Analysis(warpBpm: 128, trackBpm: 128),
            SampleRate
        );

        Assert.Equal(new TrueUpVerdict.Leave(TrueUpReason.WithinNoise), verdict);
    }

    [Fact]
    public void A_warp_with_a_step_is_left_for_the_step_to_be_fixed()
    {
        var verdict = TrueUpDecision.Of(
            SteadyTrack.Analysis(warpBpm: 101, trackBpm: 101, stepAt: 140, stepSeconds: 0.048),
            SampleRate
        );

        Assert.Equal(new TrueUpVerdict.Leave(TrueUpReason.Steps), verdict);
    }

    [Fact]
    public void A_warp_that_changes_tempo_is_left_alone()
    {
        var analysis = SteadyTrack.Analysis(warpBpm: 120, trackBpm: 120) with
        {
            Warp = new Warp { Markers = [new(0, 0), new(60, 120), new(100, 220), new(101, 222)] },
        };

        Assert.Equal(
            new TrueUpVerdict.Leave(TrueUpReason.NotSteady),
            TrueUpDecision.Of(analysis, SampleRate)
        );
    }

    [Fact]
    public void Retyping_keeps_the_first_marker_and_every_beat_and_lays_the_rest_on_one_line()
    {
        Warp warp = new() { Markers = [new(1, 0), new(61.001, 160), new(61.0025, 160.0625)] };

        var retyped = TrueUpDecision.Retyped(warp, 160);

        Assert.Equal(new WarpMarker(1, 0), retyped.Markers[0]);
        Assert.Equal([0, 160, 160.0625], retyped.Markers.Select(marker => marker.Beat));
        Assert.Equal(
            [1, 61, 61.0234375],
            retyped.Markers.Select(marker => marker.Seconds),
            Within(1e-9)
        );
    }

    private static EqualityComparer<double> Within(double tolerance) =>
        EqualityComparer<double>.Create((a, b) => Math.Abs(a - b) <= tolerance);
}
