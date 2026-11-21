using Crateside.Core.Tuning;

namespace Crateside.Tests.Tuning;

public sealed class TuningVerdictTests
{
    [Fact]
    public void Three_readings_within_six_cents_are_written_as_the_negated_median()
    {
        Assert.Equal(new TuningVerdict.Write(17.5, -17.5), TuningVerdict.Of([16, 18, 17.5]));
    }

    [Fact]
    public void A_spread_of_nine_goes_to_the_review_list_with_its_median()
    {
        Assert.Equal(new TuningVerdict.Review(20, "unsteady"), TuningVerdict.Of([15, 20, 24]));
    }

    [Fact]
    public void A_spread_of_exactly_six_is_still_steady()
    {
        Assert.IsType<TuningVerdict.Write>(TuningVerdict.Of([12, 15, 18]));
    }

    [Fact]
    public void Fewer_than_three_readings_are_unsteady()
    {
        Assert.Equal(new TuningVerdict.Review(17, "unsteady"), TuningVerdict.Of([16, 18]));
    }

    [Fact]
    public void A_median_of_9_9_is_left_in_tune()
    {
        Assert.Equal(new TuningVerdict.InTune(9.9), TuningVerdict.Of([9.9, 9.9, 9.9]));
    }

    [Fact]
    public void A_median_of_exactly_ten_is_written()
    {
        Assert.Equal(new TuningVerdict.Write(10, -10), TuningVerdict.Of([10, 10, 10]));
    }

    [Fact]
    public void A_median_of_34_5_goes_to_the_review_list_not_written()
    {
        Assert.Equal(
            new TuningVerdict.Review(34.5, "near a quarter tone"),
            TuningVerdict.Of([34, 34.5, 35])
        );
    }

    [Fact]
    public void A_median_of_exactly_thirty_is_written()
    {
        Assert.IsType<TuningVerdict.Write>(TuningVerdict.Of([30, 30, 30]));
    }

    [Fact]
    public void Unsteady_readings_that_all_sit_inside_the_threshold_are_not_reviewed()
    {
        Assert.IsType<TuningVerdict.InTune>(TuningVerdict.Of([-2, 1, 7]));
    }

    [Fact]
    public void No_readings_are_not_a_verdict()
    {
        Assert.Equal(new TuningVerdict.Review(null, "unsteady"), TuningVerdict.Of([]));
    }
}
