using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

/// <summary>The sites as they answer today; run with <c>CRATESIDE_NETWORK_TESTS=1</c>.</summary>
public sealed class SourcePagesNetworkTests
{
    private static SourcePages Pages
    {
        get
        {
            Assert.SkipUnless(
                Environment.GetEnvironmentVariable("CRATESIDE_NETWORK_TESTS") is "1",
                "CRATESIDE_NETWORK_TESTS is not set"
            );

            var client = new HttpClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(SourcePages.UserAgent);

            return new SourcePages(new FixedHttpClientFactory(client));
        }
    }

    [Fact]
    public async Task Hooktheory_money_has_two_sections_and_a_key_change_in_the_chorus()
    {
        var lookups = await Pages.FetchAsync(
            "Pink Floyd",
            "Money",
            TestContext.Current.CancellationToken
        );

        var tab = Assert.Single(lookups[0].Pages).Tab!;

        Assert.Equal(["Verse", "Chorus"], tab.Sections.Select(section => section.Name));
        Assert.Equal([1, 2], tab.Sections.Select(section => section.Keys.Count));
        Assert.InRange(tab.Coverage!.Value, 0.05, 0.08);
    }

    [Fact]
    public async Task Hooktheory_latch_has_both_its_tabs()
    {
        var lookups = await Pages.FetchAsync(
            "Disclosure",
            "Latch",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                "https://www.hooktheory.com/theorytab/view/disclosure/latch",
                "https://www.hooktheory.com/theorytab/view/disclosure-ft-sam-smith/latch",
            ],
            lookups[0].Pages.Select(page => page.Url)
        );
    }
}
