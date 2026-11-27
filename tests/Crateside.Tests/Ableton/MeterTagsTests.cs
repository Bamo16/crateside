using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

public sealed class MeterTagsTests
{
    private static MeterOutcome Settle(string[] applied, params StemMeter[] stems) =>
        MeterTags.Settle(applied, [], [], stems);

    private static StemMeter Stem(string name, string[] present, string[]? removed = null) =>
        new("song", name, present.ToHashSet(), (removed ?? []).ToHashSet());

    [Theory]
    [InlineData("Meter|4/4", "4/4")]
    [InlineData("Meter|10/4", "10/4")]
    [InlineData("Meter|Mixed", "Mixed")]
    [InlineData("Meter|Odd bars", "Odd bars")]
    public void A_meter_keyword_names_its_value(string keyword, string value) =>
        Assert.Equal(value, MeterTags.ValueOf(keyword));

    [Theory]
    [InlineData("Meter|waltz")]
    [InlineData("Meter|0/4")]
    [InlineData("Type|Vocal")]
    public void A_keyword_that_is_not_a_meter_names_none(string keyword) =>
        Assert.Null(MeterTags.ValueOf(keyword));

    [Fact]
    public void A_value_set_on_one_stem_is_wanted_on_all()
    {
        var outcome = Settle([], Stem("a|Vocal", ["4/4"]), Stem("a|Drums", []));

        Assert.Equal(["4/4"], outcome.Applied);
        Assert.False(outcome.Contradicts);
    }

    [Fact]
    public void A_value_taken_off_one_stem_is_taken_off_all_and_retired()
    {
        var outcome = Settle(["4/4", "Odd bars"], Stem("a|Vocal", ["4/4"], ["Odd bars"]));

        Assert.Equal(["4/4"], outcome.Applied);
        Assert.Equal(["Odd bars"], outcome.Retired);
        Assert.Equal(["a|Vocal|Odd bars"], outcome.RemovalsSeen);
    }

    [Fact]
    public void A_removal_already_acted_on_does_not_count_again()
    {
        var outcome = MeterTags.Settle(
            ["4/4"],
            [],
            ["a|Vocal|4/4"],
            [Stem("a|Vocal", [], ["4/4"])]
        );

        Assert.Equal(["4/4"], outcome.Applied);
    }

    [Fact]
    public void Swapping_the_base_on_one_stem_swaps_it_everywhere()
    {
        var outcome = Settle(["4/4"], Stem("a|Vocal", ["3/4"], ["4/4"]), Stem("a|Drums", ["4/4"]));

        Assert.Equal(["3/4"], outcome.Applied);
        Assert.Equal(["4/4"], outcome.Retired);
    }

    [Fact]
    public void Two_bases_are_a_contradiction_and_nothing_changes()
    {
        var outcome = Settle(["4/4"], Stem("a|Vocal", ["4/4", "3/4"]), Stem("a|Drums", ["4/4"]));

        Assert.True(outcome.Contradicts);
        Assert.Equal(["4/4"], outcome.Applied);
    }

    [Fact]
    public void Different_changes_on_different_stems_are_a_contradiction()
    {
        var outcome = Settle([], Stem("a|Vocal", ["3/4"]), Stem("a|Drums", ["5/4"]));

        Assert.True(outcome.Contradicts);
        Assert.Empty(outcome.Applied);
    }

    [Fact]
    public void Mixed_beside_a_base_is_a_contradiction() =>
        Assert.True(MeterTags.Contradicts(["Mixed", "4/4"]));

    [Fact]
    public void A_base_with_odd_bars_is_one_base() =>
        Assert.False(MeterTags.Contradicts(["6/8", "Odd bars"]));

    [Fact]
    public void Only_plain_four_four_is_not_odd()
    {
        Assert.False(MeterTags.IsOdd(["4/4"]));
        Assert.True(MeterTags.IsOdd(["4/4", "Odd bars"]));
        Assert.True(MeterTags.IsOdd(["3/4"]));
        Assert.True(MeterTags.IsOdd(["Mixed"]));
    }
}
