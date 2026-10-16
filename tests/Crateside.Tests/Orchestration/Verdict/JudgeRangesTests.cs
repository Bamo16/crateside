using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class JudgeRangesTests
{
    [Theory]
    [InlineData("33–48")]
    [InlineData("33-48")]
    [InlineData("33 48")]
    [InlineData(" 33 – 48 ")]
    public void A_bar_range_is_read_whichever_way_it_is_separated(string text)
    {
        Assert.True(JudgeRanges.TryParseBars(text, out var bars));
        Assert.Equal(new BarSpan(33, 48), bars);
    }

    [Theory]
    [InlineData("48–33")]
    [InlineData("0-4")]
    [InlineData("33")]
    [InlineData("chorus")]
    [InlineData("")]
    public void A_range_that_is_not_bars_in_order_is_refused(string text) =>
        Assert.False(JudgeRanges.TryParseBars(text, out _));

    [Theory]
    [InlineData("0:45–1:07", 45, 67)]
    [InlineData("0:45-1:07", 45, 67)]
    [InlineData("45 67", 45, 67)]
    [InlineData("1:02.5–1:34", 62.5, 94)]
    public void A_time_range_is_read_in_minutes_and_seconds_or_seconds(
        string text,
        double start,
        double end
    )
    {
        Assert.True(JudgeRanges.TryParseTimes(text, out var from, out var to));
        Assert.Equal((start, end), (from, to));
    }

    [Theory]
    [InlineData("1:07–0:45")]
    [InlineData("0:75–1:00")]
    [InlineData("soon")]
    public void A_time_range_that_is_not_a_stretch_is_refused(string text) =>
        Assert.False(JudgeRanges.TryParseTimes(text, out _, out _));

    [Fact]
    public void A_window_without_a_label_is_named_by_its_bars_or_else_its_time()
    {
        Assert.Equal(
            "bars 82–96",
            new RunWindow(112, 133, null, WindowAsker.User) { Bars = new(82, 96) }.Name
        );
        Assert.Equal("1:52–2:13", new RunWindow(112, 133, null, WindowAsker.Judge).Name);
        Assert.Equal("Chorus", new RunWindow(112, 133, "Chorus", WindowAsker.User).Name);
    }
}
