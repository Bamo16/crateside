using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Crateside.Core.Familiarity;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Familiarity;

public sealed class FamiliarityLookupTests
{
    private const string Recording1 = "5b3a4c5d-0000-0000-0000-000000000001";
    private const string Recording2 = "5b3a4c5d-0000-0000-0000-000000000002";
    private const string Elsewhere = "5b3a4c5d-0000-0000-0000-000000000003";

    private sealed class Sources(Func<string, (HttpStatusCode Status, string Body)?> answer)
        : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        public List<string> Posted { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());

            Asked.Add(url);

            if (request.Content is { } content)
                Posted.Add(await content.ReadAsStringAsync(ct));

            var (status, body) =
                answer(url) ?? throw new InvalidOperationException($"Nothing answers {url}");

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (Sources Handler, FamiliarityLookup Lookup) Lookup(
        Func<string, (HttpStatusCode, string)?> answer
    )
    {
        var handler = new Sources(answer);

        return (
            handler,
            new FamiliarityLookup(
                new FixedHttpClientFactory(new HttpClient(handler)),
                new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)),
                TimeSpan.Zero
            )
        );
    }

    private static JsonObject Claim(string target) =>
        new()
        {
            ["mainsnak"] = new JsonObject
            {
                ["snaktype"] = "value",
                ["datavalue"] = new JsonObject
                {
                    ["value"] = new JsonObject { ["entity-type"] = "item", ["id"] = target },
                },
            },
        };

    private static JsonObject Entity(
        string id,
        string label,
        string type,
        string? performer,
        string? article
    )
    {
        var claims = new JsonObject { ["P31"] = new JsonArray(Claim(type)) };

        if (performer is not null)
            claims["P175"] = new JsonArray(Claim(performer));

        var sitelinks = new JsonObject();

        if (article is not null)
            sitelinks["enwiki"] = new JsonObject { ["title"] = article };

        return new JsonObject
        {
            ["id"] = id,
            ["labels"] = new JsonObject { ["en"] = new JsonObject { ["value"] = label } },
            ["claims"] = claims,
            ["sitelinks"] = sitelinks,
        };
    }

    private static JsonObject Performer(string id, string name, params string[] aliases) =>
        new()
        {
            ["id"] = id,
            ["labels"] = new JsonObject { ["en"] = new JsonObject { ["value"] = name } },
            ["aliases"] = new JsonObject
            {
                ["en"] = new JsonArray([
                    .. aliases.Select(alias => (JsonNode)new JsonObject { ["value"] = alias }),
                ]),
            },
        };

    private static string Entities(params JsonObject[] entities)
    {
        var all = new JsonObject();

        foreach (var entity in entities)
            all[entity["id"]!.GetValue<string>()] = entity;

        return new JsonObject { ["entities"] = all }.ToJsonString();
    }

    private static string Search(params string[] ids) =>
        new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["search"] = new JsonArray([
                    .. ids.Select(id => (JsonNode)new JsonObject { ["title"] = id }),
                ]),
            },
        }.ToJsonString();

    private static string Views(params int[] months) =>
        new JsonObject
        {
            ["items"] = new JsonArray([
                .. months.Select(views => (JsonNode)new JsonObject { ["views"] = views }),
            ]),
        }.ToJsonString();

    private static string Recordings(params (string Id, string Title, string Artist)[] hits) =>
        new JsonObject
        {
            ["recordings"] = new JsonArray([
                .. hits.Select(hit =>
                    (JsonNode)
                        new JsonObject
                        {
                            ["id"] = hit.Id,
                            ["title"] = hit.Title,
                            ["artist-credit"] = new JsonArray(
                                new JsonObject { ["name"] = hit.Artist }
                            ),
                        }
                ),
            ]),
        }.ToJsonString();

    private static string Listeners(params (string Id, int? Users)[] recordings) =>
        new JsonArray([
            .. recordings.Select(one =>
                (JsonNode)
                    new JsonObject
                    {
                        ["recording_mbid"] = one.Id,
                        ["total_listen_count"] = one.Users * 9,
                        ["total_user_count"] = one.Users,
                    }
            ),
        ]).ToJsonString();

    private static (HttpStatusCode, string) Ok(string body) => (HttpStatusCode.OK, body);

    private static (HttpStatusCode, string)? Adele(string url) =>
        url switch
        {
            _ when url.Contains("musicbrainz.org") => Ok(
                Recordings((Recording1, "Hello", "Adele"), (Recording2, "Hello", "Adele"))
            ),
            _ when url.Contains("listenbrainz.org") => Ok(
                Listeners((Recording1, 8_248), (Recording2, null))
            ),
            _ when url.Contains("list=search") => Ok(Search("Q1")),
            _ when url.Contains("props=claims") => Ok(
                Entities(Entity("Q1", "Hello", "Q7366", "Q4", "Hello (Adele song)"))
            ),
            _ when url.Contains("props=labels") => Ok(Entities(Performer("Q4", "Adele"))),
            _ when url.Contains("per-article") => Ok(Views(100_000, 189_222)),
            _ => null,
        };

    [Fact]
    public async Task A_songs_listeners_and_its_own_articles_views_are_read()
    {
        var (handler, lookup) = Lookup(url =>
            url switch
            {
                _ when url.Contains("list=search") => Ok(Search("Q1", "Q2", "Q3")),
                _ when url.Contains("props=claims") => Ok(
                    Entities(
                        Entity("Q1", "Hello", "Q482994", "Q4", "Hello (Adele album)"),
                        Entity("Q2", "Hello", "Q134556", "Q9", "Hello (Martin Solveig song)"),
                        Entity("Q3", "Hello", "Q7366", "Q4", "Hello (Adele song)")
                    )
                ),
                _ when url.Contains("props=labels") => Ok(
                    Entities(Performer("Q4", "Adele"), Performer("Q9", "Martin Solveig"))
                ),
                _ => Adele(url),
            }
        );

        var reading = await lookup.ReadAsync(
            "Adele",
            "Hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new FamiliarityReading(8_248, 289_222, "Q3"), reading);
        Assert.Contains(
            handler.Asked,
            url => url.Contains("user/Hello_(Adele_song)/monthly/20251101/20261031")
        );
    }

    [Fact]
    public async Task The_listeners_are_the_most_any_of_the_songs_recordings_has_and_another_acts_are_left_out()
    {
        var (handler, lookup) = Lookup(url =>
            url switch
            {
                _ when url.Contains("musicbrainz.org") => Ok(
                    Recordings(
                        (Recording1, "Hello", "Adele"),
                        (Recording2, "Hello (Live)", "Adele"),
                        (Elsewhere, "Hello", "Lionel Richie"),
                        ("5b3a4c5d-0000-0000-0000-000000000004", "Hello Goodbye", "Adele")
                    )
                ),
                _ when url.Contains("listenbrainz.org") => Ok(
                    Listeners((Recording1, 40), (Recording2, 5_000))
                ),
                _ => Adele(url),
            }
        );

        var reading = await lookup.ReadAsync(
            "Adele",
            "Hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(5_000, reading?.Listeners);
        Assert.Equal(
            $$"""{"recording_mbids":["{{Recording1}}","{{Recording2}}"]}""",
            Assert.Single(handler.Posted)
        );
    }

    [Fact]
    public async Task A_credit_of_several_is_searched_by_its_first_artist_and_matched_on_any()
    {
        var (handler, lookup) = Lookup(url =>
            url switch
            {
                _ when url.Contains("musicbrainz.org") => Ok(
                    Recordings((Recording1, "Good Luck", "Lisa Kekaula"))
                ),
                _ when url.Contains("listenbrainz.org") => Ok(Listeners((Recording1, 39_037))),
                _ when url.Contains("list=search") => Ok(Search()),
                _ => null,
            }
        );

        var reading = await lookup.ReadAsync(
            "Basement Jaxx, Lisa Kekaula",
            "Good Luck",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(39_037, reading?.Listeners);
        Assert.Contains(
            handler.Asked,
            url => url.Contains("musicbrainz.org") && url.Contains("artist:\"Basement Jaxx\"")
        );
    }

    [Fact]
    public async Task A_song_with_no_recording_has_no_listeners_and_listenbrainz_is_not_asked()
    {
        var (handler, lookup) = Lookup(url =>
            url switch
            {
                _ when url.Contains("musicbrainz.org") => Ok(
                    Recordings((Elsewhere, "Genie", "Someone Else"))
                ),
                _ when url.Contains("list=search") => Ok(Search()),
                _ => null,
            }
        );

        var reading = await lookup.ReadAsync(
            "Busy P",
            "Genie",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new FamiliarityReading(null, 0, null), reading);
        Assert.DoesNotContain(handler.Asked, url => url.Contains("listenbrainz"));
    }

    [Fact]
    public async Task A_recording_listenbrainz_has_no_listeners_for_reads_as_no_listeners()
    {
        var (_, lookup) = Lookup(url =>
            url.Contains("listenbrainz.org") ? Ok(Listeners((Recording1, null))) : Adele(url)
        );

        var reading = await lookup.ReadAsync(
            "Adele",
            "Hello",
            TestContext.Current.CancellationToken
        );

        Assert.Null(reading?.Listeners);
    }

    [Fact]
    public async Task A_song_with_no_article_by_its_artist_has_no_views_even_if_another_acts_song_shares_the_title()
    {
        var (handler, lookup) = Lookup(url =>
            url switch
            {
                _ when url.Contains("props=claims") => Ok(
                    Entities(
                        Entity("Q1", "Waterfall", "Q7366", "Q5", "Waterfall (The Stone Roses song)")
                    )
                ),
                _ when url.Contains("props=labels") => Ok(
                    Entities(Performer("Q5", "The Stone Roses"))
                ),
                _ => Adele(url),
            }
        );

        var reading = await lookup.ReadAsync(
            "Disclosure",
            "Waterfall",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, reading?.Pageviews);
        Assert.Null(reading?.WikidataItem);
        Assert.DoesNotContain(handler.Asked, url => url.Contains("per-article"));
    }

    [Fact]
    public async Task An_artist_credited_under_an_alias_or_among_several_still_matches()
    {
        var (_, lookup) = Lookup(url =>
            url switch
            {
                _ when url.Contains("props=claims") => Ok(
                    Entities(Entity("Q1", "Genie (Busy P song)", "Q134556", "Q7", "Genie"))
                ),
                _ when url.Contains("props=labels") => Ok(
                    Entities(Performer("Q7", "Pedro Winter", "Busy P"))
                ),
                _ when url.Contains("per-article") => Ok(Views(1_000)),
                _ => Adele(url),
            }
        );

        var reading = await lookup.ReadAsync(
            "Busy P, Someone",
            "Genie (Busy P song)",
            TestContext.Current.CancellationToken
        );

        Assert.Equal((1_000, "Q1"), (reading?.Pageviews, reading?.WikidataItem));
    }

    [Fact]
    public async Task A_matching_item_with_no_english_article_has_no_views_but_is_kept()
    {
        var (handler, lookup) = Lookup(url =>
            url.Contains("props=claims")
                ? Ok(Entities(Entity("Q1", "Hello", "Q7366", "Q4", article: null)))
                : Adele(url)
        );

        var reading = await lookup.ReadAsync(
            "Adele",
            "Hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal((0, "Q1"), (reading?.Pageviews, reading?.WikidataItem));
        Assert.DoesNotContain(handler.Asked, url => url.Contains("per-article"));
    }

    [Fact]
    public async Task An_article_with_no_recorded_views_counts_as_none()
    {
        var (_, lookup) = Lookup(url =>
            url.Contains("per-article") ? (HttpStatusCode.NotFound, "{}") : Adele(url)
        );

        var reading = await lookup.ReadAsync(
            "Adele",
            "Hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal((0, "Q1"), (reading?.Pageviews, reading?.WikidataItem));
    }

    [Fact]
    public async Task Wikipedia_is_never_searched_by_title()
    {
        var (handler, lookup) = Lookup(url =>
            url.Contains("list=search") ? Ok(Search()) : Adele(url)
        );

        await lookup.ReadAsync("Disclosure", "Waterfall", TestContext.Current.CancellationToken);

        Assert.All(handler.Asked, url => Assert.DoesNotContain("wikipedia.org/w/", url));
        Assert.Contains(handler.Asked, url => url.Contains("\"Waterfall\" haswbstatement:P175"));
    }

    [Theory]
    [InlineData("musicbrainz.org")]
    [InlineData("listenbrainz.org")]
    [InlineData("list=search")]
    [InlineData("per-article")]
    public async Task A_source_that_cannot_be_reached_is_a_failure_to_try_again_not_a_song_it_does_not_know(
        string refusing
    )
    {
        var (_, lookup) = Lookup(url =>
            url.Contains(refusing) ? (HttpStatusCode.TooManyRequests, "{}") : Adele(url)
        );

        Assert.Null(
            await lookup.ReadAsync("Adele", "Hello", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Wikidata_answering_with_an_error_is_a_failure_to_try_again()
    {
        var (_, lookup) = Lookup(url =>
            url.Contains("wikidata") ? Ok("""{"error":{"code":"maxlag"}}""") : Adele(url)
        );

        Assert.Null(
            await lookup.ReadAsync("Adele", "Hello", TestContext.Current.CancellationToken)
        );
    }
}
