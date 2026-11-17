using Crateside.Core.Pairing;

namespace Crateside.Tests.Pairing;

public sealed class ShiftRangeTests
{
    [Fact]
    public void One_either_side_gives_three_rows_highest_first() =>
        Assert.Equal([1, 0, -1], new ShiftRange(-1, 1, RePitch.MaxShift).Rows());

    [Fact]
    public void The_widest_range_gives_25_rows_for_Re_Pitch_and_5_for_Partners()
    {
        Assert.Equal(25, new ShiftRange(-12, 12, RePitch.MaxShift).Rows().Count());
        Assert.Equal(5, new ShiftRange(-2, 2, Partners.WidestShift).Rows().Count());
    }

    [Fact]
    public void Zero_to_zero_gives_one_row() => Assert.Equal([0], new ShiftRange(0, 0, 2).Rows());

    [Fact]
    public void The_low_handle_cannot_pass_zero() =>
        Assert.Equal(new ShiftRange(0, 1, 12), new ShiftRange(-1, 1, 12).WithLowest(3));

    [Fact]
    public void The_high_handle_cannot_pass_zero_or_the_maximum()
    {
        var range = new ShiftRange(-1, 1, 2);

        Assert.Equal(0, range.WithHighest(-4).Highest);
        Assert.Equal(2, range.WithHighest(9).Highest);
    }

    [Fact]
    public void A_reversed_pair_from_the_query_clamps_without_throwing()
    {
        var range = ShiftRange.Parse(2, -2, 2, 0);

        Assert.Equal((0, 0), (range.Lowest, range.Highest));
    }

    [Fact]
    public void An_out_of_range_end_clamps_to_the_maximum()
    {
        var range = ShiftRange.Parse(-99, null, 2, 0);

        Assert.Equal((-2, 0), (range.Lowest, range.Highest));
    }

    [Fact]
    public void A_missing_pair_falls_back_to_the_default() =>
        Assert.Equal([1, 0, -1], ShiftRange.Parse(null, null, 12, 1).Rows());

    [Fact]
    public void Includes_covers_exactly_the_rows()
    {
        var range = new ShiftRange(-1, 2, 12);

        Assert.All([-1, 0, 1, 2], shift => Assert.True(range.Includes(shift)));
        Assert.All([-2, 3], shift => Assert.False(range.Includes(shift)));
    }
}
