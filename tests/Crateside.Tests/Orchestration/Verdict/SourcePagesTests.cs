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

    private const string MoneyVerse = "jDgX__kw_mK";
    private const string MoneyChorus = "VMgJXXWOKoq";

    private static Dictionary<string, string> MoneyScores =>
        new()
        {
            [MoneyVerse] = Fixture($"hooktheory-api-{MoneyVerse}.json"),
            [MoneyChorus] = Fixture($"hooktheory-api-{MoneyChorus}.json"),
        };

    private static HooktheoryTab Money(Dictionary<string, string>? scores = null) =>
        SourcePages
            .HooktheoryPage(
                "https://www.hooktheory.com/theorytab/view/pink-floyd/money",
                Fixture("hooktheory-money.html"),
                scores ?? MoneyScores
            )
            .Tab!;

    [Fact]
    public void A_key_change_inside_a_section_is_kept_with_its_beat()
    {
        var tab = Money();

        Assert.Equal([new TabKey(1, "B dorian")], tab.Sections[0].Keys);
        Assert.Equal([new TabKey(1, "B minor"), new TabKey(15, "B dorian")], tab.Sections[1].Keys);
        Assert.Equal([29, 13], tab.Sections.Select(section => section.Chords));
    }

    [Fact]
    public void Coverage_is_the_share_of_the_synced_video_the_sections_span()
    {
        var tab = Money();

        Assert.Equal(0.0628, Assert.NotNull(tab.Coverage), 4);
        Assert.Equal(0.1057, Assert.NotNull(tab.Sections[0].SyncStart), 4);
        Assert.Equal(0.1420, Assert.NotNull(tab.Sections[0].SyncEnd), 4);
    }

    [Fact]
    public void A_section_synced_to_another_video_does_not_count_toward_coverage()
    {
        var scores = MoneyScores;

        scores[MoneyChorus] = scores[MoneyChorus].Replace("2aW7HweAf3o", "another-video");

        var tab = Money(scores);

        Assert.Equal(0.0363, Assert.NotNull(tab.Coverage), 4);
        Assert.Equal("another-video", tab.Sections[1].VideoId);
        Assert.Null(tab.Sections[0].VideoId);
    }

    [Fact]
    public void A_tab_whose_sections_did_not_answer_still_lists_them_without_coverage()
    {
        var tab = Money([]);

        Assert.Equal(["Verse", "Chorus"], tab.Sections.Select(section => section.Name));
        Assert.Null(tab.Coverage);
        Assert.All(tab.Sections, section => Assert.Empty(section.Keys));
    }

    [Fact]
    public async Task Each_section_is_asked_of_the_api_once_and_one_that_fails_is_left_unread()
    {
        var handler = new RecordedSites(
            new()
            {
                ["www.hooktheory.com"] = Fixture("hooktheory-money.html"),
                [$"api.hooktheory.com/v1/songs/public/{MoneyVerse}"] = Fixture(
                    $"hooktheory-api-{MoneyVerse}.json"
                ),
            },
            failing: "api.hooktheory.com",
            failingPath: MoneyChorus
        );

        var lookups = await new SourcePages(
            new FixedHttpClientFactory(new HttpClient(handler))
        ).FetchAsync("Pink Floyd", "Money", TestContext.Current.CancellationToken);

        var tab = Assert.Single(lookups[0].Pages).Tab!;

        Assert.Equal(LookupStatus.Found, lookups[0].Status);
        Assert.Equal(2, handler.Requests.Count(uri => uri.Host == "api.hooktheory.com"));
        Assert.Equal(0.0363, Assert.NotNull(tab.Coverage), 4);
        Assert.Empty(tab.Sections[1].Keys);
    }

    [Theory]
    [InlineData(
        "Disclosure",
        "Latch",
        "hooktheory-search-disclosure-latch.json",
        new[] { "disclosure/latch", "disclosure-ft-sam-smith/latch" }
    )]
    [InlineData(
        "Billy Joel",
        "Piano Man",
        "hooktheory-search-billy-joel-piano-man.json",
        new[] { "billy-joel/piano-man", "billyjoel/piano-man" }
    )]
    [InlineData(
        "Disclosure ft Sam Smith",
        "Latch",
        "hooktheory-search-disclosure-latch.json",
        new[] { "disclosure/latch", "disclosure-ft-sam-smith/latch" }
    )]
    public void Every_tab_the_search_files_under_the_song_is_found_but_not_its_remixes_or_other_songs(
        string artist,
        string title,
        string fixture,
        string[] tabs
    ) =>
        Assert.Equal(
            tabs.Select(tab => $"https://www.hooktheory.com/theorytab/view/{tab}"),
            SourcePages.HooktheoryTabUrls(Fixture(fixture), artist, title)
        );

    [Fact]
    public void A_search_answer_that_is_not_json_finds_no_tabs() =>
        Assert.Empty(SourcePages.HooktheoryTabUrls("<html>blocked</html>", "Disclosure", "Latch"));

    [Fact]
    public async Task Both_tabs_of_a_song_are_read_each_with_its_own_sections()
    {
        var handler = new RecordedSites(
            new()
            {
                ["www.hooktheory.com/js/meilisearch"] = Fixture("hooktheory-search-script.js"),
                ["www.hooktheory.com/meili"] = Fixture("hooktheory-search-disclosure-latch.json"),
                ["www.hooktheory.com/theorytab/view/disclosure/latch"] = Fixture(
                    "hooktheory-money.html"
                ),
                ["www.hooktheory.com/theorytab/view/disclosure-ft-sam-smith/latch"] = Fixture(
                    "hooktheory-piano-man.html"
                ),
                [$"api.hooktheory.com/v1/songs/public/{MoneyVerse}"] = Fixture(
                    $"hooktheory-api-{MoneyVerse}.json"
                ),
            }
        );

        var lookups = await new SourcePages(
            new FixedHttpClientFactory(new HttpClient(handler))
        ).FetchAsync("Disclosure", "Latch", TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "https://www.hooktheory.com/theorytab/view/disclosure/latch",
                "https://www.hooktheory.com/theorytab/view/disclosure-ft-sam-smith/latch",
            ],
            lookups[0].Pages.Select(page => page.Url)
        );
        Assert.Equal([2, 6], lookups[0].Pages.Select(page => page.Tab!.Sections.Count));
        Assert.Equal(["test-search-key"], handler.Bearers);
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
    public void Musicnotes_tells_a_user_arrangement_from_a_licensed_edition_by_its_arranger()
    {
        var arrangement = SourcePages.MusicnotesPage(
            "https://www.musicnotes.com/sheetmusic/francesco-parrino/piano-man/MN0232398",
            Fixture("musicnotes-user-arrangement.html")
        );
        var licensed = SourcePages.MusicnotesPage(
            "https://www.musicnotes.com/sheetmusic/daft-punk/around-the-world/MN0208575",
            Fixture("musicnotes-around-the-world.html")
        );

        Assert.Equal("user arrangement by Parrino, Francesco", arrangement.Note);
        Assert.Null(licensed.Note);
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
    public async Task Each_site_is_asked_for_its_page_and_a_missing_page_is_simply_absent()
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

        var lookups = await new SourcePages(
            new FixedHttpClientFactory(new HttpClient(handler))
        ).FetchAsync("Daft Punk", "Around the World", TestContext.Current.CancellationToken);

        Assert.Equal(
            [LookupStatus.NotFound, LookupStatus.Found, LookupStatus.NotFound],
            lookups.Select(lookup => lookup.Status)
        );
        Assert.Equal(
            [
                "www.hooktheory.com",
                "www.hooktheory.com",
                "www.musicnotes.com",
                "www.musicnotes.com",
                "www.ultimate-guitar.com",
            ],
            handler.Requests.Select(uri => uri.Host).Order()
        );
    }

    [Theory]
    [InlineData("Around the World (Radio Edit)")]
    [InlineData("Around the World [Remastered] (feat. Nobody)")]
    public async Task A_bracketed_tail_on_the_title_is_left_out_of_the_lookup(string title)
    {
        var handler = new RecordedSites(new() { ["www.hooktheory.com"] = null });

        await new SourcePages(new FixedHttpClientFactory(new HttpClient(handler))).FetchAsync(
            "Daft Punk",
            title,
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath == "/theorytab/view/daft-punk/around-the-world"
        );
    }

    [Fact]
    public async Task A_site_that_cannot_be_reached_says_so_and_the_others_still_answer()
    {
        var handler = new RecordedSites(
            new() { ["www.musicnotes.com/search"] = string.Empty },
            failing: "www.hooktheory.com"
        );

        var lookups = await new SourcePages(
            new FixedHttpClientFactory(new HttpClient(handler))
        ).FetchAsync("Daft Punk", "Around the World", TestContext.Current.CancellationToken);

        Assert.Equal(LookupStatus.Unreachable, lookups[0].Status);
        Assert.NotNull(lookups[0].Error);
        Assert.Equal(LookupStatus.NotFound, lookups[1].Status);
    }

    /// <summary>Serves a page by host and path prefix; null is a 404, and an unlisted site 404s too.</summary>
    private sealed class RecordedSites(
        Dictionary<string, string?> pages,
        string? failing = null,
        string? failingPath = null
    ) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public List<string> Bearers { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var uri = request.RequestUri!;

            Requests.Add(uri);

            if (request.Headers.Authorization is { Parameter: { } bearer })
                Bearers.Add(bearer);

            if (
                uri.Host == failing
                && (failingPath is null || uri.AbsolutePath.Contains(failingPath))
            )
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
