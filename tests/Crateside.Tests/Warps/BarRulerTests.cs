using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class BarRulerTests
{
    private static readonly BarRuler FourFour = BarRuler.Of(new TimeSignature(4, 4));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3.99, 1)]
    [InlineData(4, 2)]
    [InlineData(132, 34)]
    [InlineData(-0.5, -1)]
    [InlineData(-4, -1)]
    [InlineData(-4.5, -2)]
    public void Beat_0_is_bar_1_and_the_bar_before_it_is_minus_1(double beat, int bar) =>
        Assert.Equal(bar, FourFour.BarAt(beat));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(34, 132)]
    [InlineData(-1, -4)]
    [InlineData(-2, -8)]
    public void A_bar_starts_on_its_beat_across_the_missing_bar_0(int bar, double beat) =>
        Assert.Equal(beat, FourFour.StartOf(bar));

    [Theory]
    [InlineData(132, "34")]
    [InlineData(131.9999999, "34")]
    [InlineData(86.75, "22.3.4")]
    [InlineData(0, "1")]
    [InlineData(-1, "-1.4.1")]
    [InlineData(-3.75, "-1.1.2")]
    public void A_position_reads_as_Lives_start_field_writes_it(double beat, string position) =>
        Assert.Equal(position, FourFour.Position(beat));

    [Fact]
    public void A_six_eight_bar_is_six_eighths_and_its_positions_count_eighths()
    {
        var ruler = BarRuler.Of(new TimeSignature(6, 8));

        Assert.Equal(3, ruler.BeatsPerBar);
        Assert.Equal(4, ruler.BarAt(9));
        Assert.Equal("4.3.1", ruler.Position(10));
    }

    [Fact]
    public void A_signature_Live_never_wrote_reads_as_four_four() =>
        Assert.Equal(new BarRuler(4, 4), BarRuler.Of(new TimeSignature(0, 0)));
}
