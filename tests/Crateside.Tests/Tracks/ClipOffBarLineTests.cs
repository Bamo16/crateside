using AbleKit.Analysis;
using Crateside.Core.Ableton;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;

namespace Crateside.Tests.Tracks;

/// <summary>A default clip whose start is not on a bar line of Live's ruler (#177).</summary>
public sealed class ClipOffBarLineTests
{
    private static readonly BarRuler FourFour = BarRuler.Of(new TimeSignature(4, 4));

    [Theory]
    [InlineData(132, true, "34")]
    [InlineData(0, true, "1")]
    [InlineData(-4, true, "-1")]
    [InlineData(-8, true, "-2")]
    [InlineData(131.9999999, true, "34")]
    [InlineData(130.25, false, "33.3.2")]
    [InlineData(132.5, false, "34.1.3")]
    [InlineData(-3.5, false, "-1.1.3")]
    [InlineData(-6, false, "-2.3.1")]
    public void A_clip_start_is_on_a_bar_line_only_where_Lives_start_field_reads_a_whole_bar(
        double beat,
        bool onLine,
        string position
    )
    {
        var start = ClipStartPosition.Of(beat, FourFour);

        Assert.Equal(onLine, start.OnBarLine);
        Assert.Equal(position, start.Position);
    }

    [Fact]
    public void A_six_eight_bar_is_six_eighths_long()
    {
        var sixEight = BarRuler.Of(new TimeSignature(6, 8));

        Assert.True(ClipStartPosition.Of(6, sixEight).OnBarLine);
        Assert.False(ClipStartPosition.Of(4, sixEight).OnBarLine);
    }

    [Fact]
    public void A_row_is_flagged_when_any_sample_starts_its_clip_off_a_bar_line()
    {
        var row = Row(Sample(132), Sample(130.25));

        Assert.True(row.ClipStartsOffBarLine);
        Assert.True(row.Matches(TrackFilter.ClipOffBarLine));
        Assert.Equal([row], new[] { row }.Narrow(TrackFilter.ClipOffBarLine));
    }

    [Fact]
    public void A_row_whose_clips_all_start_on_a_bar_line_is_not()
    {
        var row = Row(Sample(132), Sample(0));

        Assert.False(row.ClipStartsOffBarLine);
        Assert.Empty(new[] { row }.Narrow(TrackFilter.ClipOffBarLine));
    }

    [Fact]
    public void A_sample_with_no_saved_clip_says_nothing()
    {
        Assert.False(Row(new TrackSample { Label = "Vocal - Full" }).ClipStartsOffBarLine);
    }

    private static TrackRow Row(params TrackSample[] samples) =>
        new()
        {
            TrackKey = "aaaaaaaaaaa",
            Title = "Track one",
            Samples = samples,
        };

    private static TrackSample Sample(double clipStartBeat) =>
        new()
        {
            Label = "Vocal - Full",
            Tempo = new WarpTempo(120, TempoShape.Steady, 120, 120, [])
            {
                ClipStart = ClipStartPosition.Of(clipStartBeat, FourFour),
            },
        };
}
