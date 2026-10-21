using System.Net;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

/// <summary>The three sites read from pages recorded on 2026-10-16.</summary>
public sealed class SourcePagesTests
{
    private static readonly string Fixtures = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "SourcePages"
    );

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(Fixtures, name));

    [Fact]
    public void Hooktheory_gives_each_sections_key_and_tempo()
    {
        var page = SourcePages.HooktheoryPage(
            "https://www.hooktheory.com/theorytab/view/daft-punk/around-the-world",
            Fixture("hooktheory-around-the-world.html")
        );

        Assert.Equal(
            [new PageKey("A dorian", "Verse", 121), new PageKey("E minor", "Chorus", 121)],
            page.Keys
        );
        Assert.StartsWith("Around the World by Daft Punk", page.Heading);
    }

    [Fact]
    public void Hooktheory_leaves_the_sites_own_account_out_of_the_contributors()
    {
        var tab = SourcePages
            .HooktheoryPage(
                "https://www.hooktheory.com/theorytab/view/billy-joel/piano-man",
                Fixture("hooktheory-piano-man.html")
            )
            .Tab;

        Assert.NotNull(tab);
        Assert.Equal(18, tab.Contributors.Count);
        Assert.DoesNotContain("Hooktheory", tab.Contributors);
        Assert.Contains("socsy", tab.Contributors);
        Assert.Equal(
            ["Instrumental Intro", "Intro", "Verse", "Pre-Chorus", "Chorus", "Instrumental"],
            tab.Sections.Select(section => section.Name)
        );
        Assert.Equal("gxEPV4kolz0", tab.SyncedVideoId);
        Assert.Equal("C major", tab.AllKeys);
    }

    [Fact]
    public void Hooktheory_gives_each_sections_progression_and_how_much_of_it_is_filled_in()
    {
        var tab = SourcePages
            .HooktheoryPage(
                "https://www.hooktheory.com/theorytab/view/pink-floyd/money",
                Fixture("hooktheory-money.html")
            )
            .Tab;

        Assert.NotNull(tab);
        Assert.Equal(["Artman", "JerichoScott", "Baraniscool"], tab.Contributors);
        Assert.Equal("B dorian, B minor", tab.AllKeys);

        var verse = tab.Sections[0];

        Assert.StartsWith("i7 i43 i7 i64 i42 i IV42", verse.Progression);
        Assert.Equal(84.88517, verse.PercentChord);
        Assert.Equal(46.360126, verse.PercentMelody);
    }

    [Fact]
    public void A_hooktheory_page_without_a_song_payload_has_no_tab()
    {
        var page = SourcePages.HooktheoryPage(
            "https://www.hooktheory.com/theorytab/view/a/b",
            "<html><head><title>x</title></head><body>nothing</body></html>"
        );

        Assert.Null(page.Tab);
        Assert.Empty(page.Keys);
    }

    [Fact]
    public void Musicnotes_is_read_from_the_original_artists_product_not_a_cover()
    {
        var product = SourcePages.MusicnotesProduct(
            Fixture("musicnotes-search-around-the-world.html"),
            "Daft Punk",
            "Around the World"
        );

        Assert.Equal(
            "https://www.musicnotes.com/sheetmusic/daft-punk/around-the-world/MN0208575",
            product
        );

        var page = SourcePages.MusicnotesPage(
            product!,
            Fixture("musicnotes-around-the-world.html")
        );

        Assert.Equal(
            new PageKey("E Minor", "Original Published Key", null),
            Assert.Single(page.Keys)
        );
        Assert.Equal("Piano/Vocal/Guitar, Singer Pro", page.Arrangement);
    }

    [Fact]
    public void Musicnotes_older_product_layout_gives_its_key_too()
    {
        var page = SourcePages.MusicnotesPage(
            "https://www.musicnotes.com/sheetmusic/nelly/hot-in-herre/MN0103472",
            Fixture("musicnotes-hot-in-herre.html")
        );

        Assert.Equal("E Minor", Assert.Single(page.Keys).Key);
    }

    [Fact]
    public void Ultimate_guitar_keeps_the_songs_tabs_that_state_a_key_then_its_most_voted_chords()
    {
        var pages = SourcePages.UltimateGuitarPages(
            Fixture("ultimate-guitar-owner-of-a-lonely-heart.html"),
            "Yes",
            "Owner of a Lonely Heart"
        );

        Assert.Equal(
            [
                "https://tabs.ultimate-guitar.com/tab/yes/owner-of-a-lonely-heart-official-2380805",
                "https://tabs.ultimate-guitar.com/tab/yes/owner-of-a-lonely-heart-guitar-pro-2585739",
                "https://tabs.ultimate-guitar.com/tab/yes/owner-of-a-lonely-heart-chords-410188",
            ],
            pages.Select(page => page.Url)
        );
        Assert.Equal("Am", Assert.Single(pages[0].Keys).Key);
        Assert.Empty(pages[2].Keys);
    }

    [Theory]
    [InlineData("Beyoncé", "beyonce")]
    [InlineData("Don't Stop Me Now", "dont-stop-me-now")]
    [InlineData("perfume!", "perfume")]
    [InlineData("AC/DC", "ac-dc")]
    public void A_name_becomes_the_slug_the_sites_use(string name, string slug) =>
        Assert.Equal(slug, SourcePages.Slug(name));

    [Fact]
    public async Task Each_site_is_asked_once_and_a_missing_page_is_simply_absent()
    {
        var handler = new RecordedSites(
            new()
            {
                ["www.hooktheory.com"] = null,
                ["www.musicnotes.com/search"] = Fixture("musicnotes-search-around-the-world.html"),
                ["www.musicnotes.com/sheetmusic"] = Fixture("musicnotes-around-the-world.html"),
                ["www.ultimate-guitar.com"] = "<html>no store</html>",
            }
        );

        var lookups = await new SourcePages(() => new HttpClient(handler)).FetchAsync(
            "Daft Punk",
            "Around the World",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [LookupStatus.NotFound, LookupStatus.Found, LookupStatus.NotFound],
            lookups.Select(lookup => lookup.Status)
        );
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task A_site_that_cannot_be_reached_says_so_and_the_others_still_answer()
    {
        var handler = new RecordedSites(
            new() { ["www.musicnotes.com/search"] = string.Empty },
            failing: "www.hooktheory.com"
        );

        var lookups = await new SourcePages(() => new HttpClient(handler)).FetchAsync(
            "Daft Punk",
            "Around the World",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(LookupStatus.Unreachable, lookups[0].Status);
        Assert.NotNull(lookups[0].Error);
        Assert.Equal(LookupStatus.NotFound, lookups[1].Status);
    }

    /// <summary>Serves a page by host and path prefix; null is a 404, and an unlisted site 404s too.</summary>
    private sealed class RecordedSites(Dictionary<string, string?> pages, string? failing = null)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var uri = request.RequestUri!;

            Requests.Add(uri);

            if (uri.Host == failing)
                throw new HttpRequestException("connection refused");

            var page = pages
                .Where(entry =>
                    $"{uri.Host}{uri.AbsolutePath}".StartsWith(entry.Key, StringComparison.Ordinal)
                )
                .Select(entry => entry.Value)
                .FirstOrDefault();

            return Task.FromResult(
                page is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(page),
                    }
            );
        }
    }
}
