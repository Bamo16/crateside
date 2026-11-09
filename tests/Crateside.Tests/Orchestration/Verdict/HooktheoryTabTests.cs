using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class HooktheoryTabTests
{
    private static HooktheoryTab Tab(int contributors, int sections) =>
        new()
        {
            Contributors = [.. Enumerable.Range(1, contributors).Select(i => $"user{i}")],
            Sections =
            [
                .. Enumerable
                    .Range(1, sections)
                    .Select(i => new TabSection($"Part {i}", null, null, null)),
            ],
        };

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(1, 3, false)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    public void A_tab_is_thin_only_with_one_contributor_and_one_or_two_sections(
        int contributors,
        int sections,
        bool thin
    ) => Assert.Equal(thin, Tab(contributors, sections).IsThin);

    [Fact]
    public void A_thin_tab_is_marked_in_the_work_order()
    {
        var lines = JudgePrompt.PagesLines([
            new(
                "Hooktheory",
                LookupStatus.Found,
                [
                    new FetchedPage
                    {
                        Site = "Hooktheory",
                        Url = "https://www.hooktheory.com/theorytab/view/x/y",
                        Tab = Tab(1, 1),
                    },
                ]
            ),
        ]);

        Assert.Contains("  - THIN PAGE: one contributor and one or two sections", lines);
    }
}
