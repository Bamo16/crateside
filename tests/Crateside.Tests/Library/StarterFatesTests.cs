using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Library;

namespace Crateside.Tests.Library;

public sealed class StarterFatesTests
{
    // A steady 120 BPM warp whose beat 0 is at 0.25 s, so a beat falls every half second.
    private static readonly WarpTempo Warp = new(120, TempoShape.Steady, 120, 120, [])
    {
        Line = new GridLine(0.25, 0, 0.25 + 64 * 0.5, 64),
    };

    // The same grid, numbered so that the first beat is beat 1 of a bar.
    private static readonly WarpTempo SecondBeat = Warp with
    {
        Line = new GridLine(0.25, 1, 0.25 + 64 * 0.5, 65),
    };

    private static BeatReading Reading(
        double bpm = 120,
        double beatMs = 250,
        double? downbeatMs = 250,
        double confidence = 0.9
    ) =>
        new()
        {
            Bpm = bpm,
            BeatOffsetMs = beatMs,
            DownbeatOffsetMs = downbeatMs,
            DownbeatConfidence = confidence,
            BeatsPerBar = 4,
            Windows = [.. Enumerable.Repeat(bpm, 8)],
        };

    [Fact]
    public void A_marker_on_a_beat_in_the_right_bar_is_kept()
    {
        Assert.Equal(StarterFate.Kept, StarterFates.Of(Reading(), Warp));
    }

    [Fact]
    public void A_downbeat_the_finished_warp_numbers_otherwise_was_corrected()
    {
        // The reading says the downbeat is one beat on; the maintainer's bar line is at beat 0.
        Assert.Equal(StarterFate.BarCorrected, StarterFates.Of(Reading(downbeatMs: 750), Warp));
    }

    [Fact]
    public void A_downbeat_under_the_gate_leaves_the_marker_on_beat_0_whatever_the_bar_is()
    {
        Assert.Equal(
            StarterFate.LeftAtBeatZero,
            StarterFates.Of(Reading(downbeatMs: 1750, confidence: 0.3), SecondBeat)
        );
        Assert.Equal(StarterFate.Kept, StarterFates.Of(Reading(confidence: 0.3), Warp));
    }

    [Fact]
    public void A_trusted_downbeat_numbers_the_marker_so_a_first_beat_that_is_not_beat_1_is_kept()
    {
        Assert.Equal(
            StarterFate.Kept,
            StarterFates.Of(Reading(downbeatMs: 1750, confidence: 0.9), SecondBeat)
        );
    }

    [Fact]
    public void A_marker_between_the_finished_warps_beats_was_moved()
    {
        Assert.Equal(StarterFate.BeatMoved, StarterFates.Of(Reading(beatMs: 400), Warp));
        Assert.Equal(StarterFate.BeatMoved, StarterFates.Of(Reading(bpm: 126), Warp));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    public void A_starter_at_half_or_double_the_tempo_was_retyped(double bpm) =>
        Assert.Equal(StarterFate.OtherOctave, StarterFates.Of(Reading(bpm), Warp));

    [Fact]
    public void A_reading_that_drifts_gives_no_starter()
    {
        var drifting = Reading() with { Windows = [118, 119, 120, 121, 122, 123] };

        Assert.Equal(StarterFate.NoStarter, StarterFates.Of(drifting, Warp));
    }

    [Fact]
    public void Nothing_is_judged_without_a_reading_or_a_steady_warp()
    {
        Assert.Null(StarterFates.Of(null, Warp));
        Assert.Null(StarterFates.Of(Reading(), Warp with { Shape = TempoShape.Drifting }));
        Assert.Null(StarterFates.Of(Reading(), null));
    }

    [Fact]
    public void Songs_are_counted_in_the_bucket_of_their_fate_and_the_rest_are_left_out()
    {
        var counts = StarterWarpCounts.From([
            Song("b", StarterFate.Kept),
            Song("a", StarterFate.Kept),
            Song("c", StarterFate.OtherOctave),
            Song("d", null),
        ]);

        Assert.Equal(3, counts.Compared);
        Assert.Equal(1, counts.NotCompared);
        Assert.Equal(["a", "b"], counts.Buckets[0].Songs.Select(song => song.Name));
        Assert.Equal(StarterFate.Kept, counts.Buckets[0].Fate);
        Assert.Equal(6, counts.Buckets.Count);
    }

    private static LibrarySong Song(string name, StarterFate? fate) =>
        new()
        {
            Song = name,
            Name = name,
            Starter = fate,
        };
}
