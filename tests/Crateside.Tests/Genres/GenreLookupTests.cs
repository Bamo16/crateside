using System.Net;
using System.Text;
using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

public sealed class GenreLookupTests
{
    private const string Recording = """{ "recordings": [ { "id": "rec-1" } ] }""";

    private const string RecordingDetail = """
        {
          "genres": [
            { "name": "pop", "count": 1 },
            { "name": "synth-pop", "count": 9 },
            { "name": "dance", "count": 4 }
          ],
          "artist-credit": [ { "artist": { "id": "art-1" } } ]
        }
        """;

    private const string ArtistDetail = """
        { "genres": [ { "name": "Pop", "count": 2 }, { "name": "new wave", "count": 7 } ] }
        """;

    private const string DeezerSearch = """
        { "data": [ { "id": 10, "title": "Song", "rank": 5,
            "artist": { "name": "Band" }, "album": { "id": 77 } } ] }
        """;

    private const string DeezerAlbum = """
        { "genres": { "data": [ { "name": "Rock" }, { "name": "Dance" } ] } }
        """;

    private sealed class Sources(Func<string, (HttpStatusCode Status, string Body)?> answer)
        : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());

            Asked.Add(url);

            var (status, body) =
                answer(url) ?? throw new InvalidOperationException($"Nothing answers {url}");

            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private static (HttpStatusCode, string)? Everything(string url, string itunes) =>
        url switch
        {
            _ when url.Contains("/ws/2/recording?query=") => (HttpStatusCode.OK, Recording),
            _ when url.Contains("/ws/2/recording/rec-1") => (HttpStatusCode.OK, RecordingDetail),
            _ when url.Contains("/ws/2/artist/art-1") => (HttpStatusCode.OK, ArtistDetail),
            _ when url.Contains("api.deezer.com/search") => (HttpStatusCode.OK, DeezerSearch),
            _ when url.Contains("api.deezer.com/album/77") => (HttpStatusCode.OK, DeezerAlbum),
            _ when url.Contains("itunes.apple.com/search") => (HttpStatusCode.OK, itunes),
            _ => null,
        };

    private static GenreLookup Lookup(Sources sources) =>
        new(new FixedHttpClientFactory(new HttpClient(sources)));

    [Fact]
    public async Task Every_source_is_heard_in_order_with_the_most_voted_genres_first_and_repeats_dropped()
    {
        var sources = new Sources(url =>
            Everything(
                url,
                """{ "results": [ { "artistName": "Band", "primaryGenreName": "Alternative" } ] }"""
            )
        );

        var hearing = await Lookup(sources)
            .HearAsync("Band", "Song", TestContext.Current.CancellationToken);

        Assert.NotNull(hearing);
        // Recording by votes, then the artist's (pop already heard, case aside), Deezer's album, then the storefront.
        Assert.Equal(
            ["synth-pop", "dance", "pop", "new wave", "Rock", "Alternative"],
            hearing.Labels
        );
        Assert.Equal("rec-1", hearing.RecordingMbid);
    }

    [Fact]
    public async Task A_storefront_hit_by_another_artist_lends_nothing()
    {
        var sources = new Sources(url =>
            Everything(
                url,
                """{ "results": [ { "artistName": "Somebody Else", "primaryGenreName": "Karaoke" } ] }"""
            )
        );

        var hearing = await Lookup(sources)
            .HearAsync("The Band", "Song", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Karaoke", hearing!.Labels);
    }

    [Fact]
    public async Task A_song_musicbrainz_does_not_know_still_hears_the_other_sources_and_has_no_mbid()
    {
        var sources = new Sources(url =>
            url.Contains("/ws/2/recording?query=")
                ? (HttpStatusCode.OK, """{ "recordings": [] }""")
                : Everything(url, """{ "results": [] }""")
        );

        var hearing = await Lookup(sources)
            .HearAsync("Band", "Song", TestContext.Current.CancellationToken);

        Assert.Equal(["Rock", "Dance"], hearing!.Labels);
        Assert.Null(hearing.RecordingMbid);
        Assert.DoesNotContain(sources.Asked, url => url.Contains("/ws/2/recording/"));
    }

    [Fact]
    public async Task A_source_that_cannot_be_reached_gives_null_so_it_is_asked_again()
    {
        var sources = new Sources(url =>
            url.Contains("/ws/2/recording?query=")
                ? (HttpStatusCode.OK, """{ "recordings": [] }""")
                : (HttpStatusCode.ServiceUnavailable, string.Empty)
        );

        Assert.Null(
            await Lookup(sources).HearAsync("Band", "Song", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Quotes_in_the_names_are_dropped_from_the_musicbrainz_query()
    {
        var sources = new Sources(url =>
            url.Contains("/ws/2/recording?query=")
                ? (HttpStatusCode.OK, """{ "recordings": [] }""")
                : Everything(url, """{ "results": [] }""")
        );

        await Lookup(sources)
            .HearAsync("The \"Band\"", "A \"Song\"", TestContext.Current.CancellationToken);

        Assert.Contains(
            sources.Asked,
            url => url.Contains("recording:\"A Song\" AND artist:\"The Band\"")
        );
    }
}
