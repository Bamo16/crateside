using System.Net;
using System.Text;
using Crateside.Core.Familiarity;

namespace Crateside.Tests.Familiarity;

public class FamiliarityVocabularyTests
{
    [Theory]
    // The user's own examples, measured on #52.
    [InlineData(19_119_697L, 748_192, "Mainstream")] // Eminem
    [InlineData(33_374L, 625_143, "Mainstream")] // Cali Swag District, a one-hit wonder
    [InlineData(15_544L, 347_759, "Obscure")] // Snail's House
    [InlineData(17_522L, 167_703, "Obscure")] // Moe Shop
    [InlineData(10_000L, null, "Obscure")]
    [InlineData(48_382L, 300_000, null)] // the middle band asks
    [InlineData(null, 500_000, null)]
    public void The_artist_decides_first_then_the_song(long? fans, int? rank, string? expected) =>
        Assert.Equal(expected, FamiliarityVocabulary.FromReach(fans, rank));
}

public class FamiliarityStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string StorePath => Path.Combine(_root, "familiarity.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_measured_song_suggests_and_writes_what_the_numbers_settle()
    {
        new FamiliarityStore(StorePath).Measure(
            "song",
            new FamiliarityReading("Moe Shop", 17_522, 167_703),
            DateTimeOffset.UtcNow
        );

        var record = new FamiliarityStore(StorePath).Read()["song"];

        Assert.Equal("Obscure", record.Suggested);
        Assert.Equal("Obscure", record.Written);
    }

    [Fact]
    public void The_users_answer_outranks_the_numbers_and_survives_a_new_measurement()
    {
        var store = new FamiliarityStore(StorePath);

        store.Settle("song", "Mainstream");
        store.Measure(
            "song",
            new FamiliarityReading("Moe Shop", 17_522, 167_703),
            DateTimeOffset.UtcNow
        );

        Assert.Equal("Mainstream", new FamiliarityStore(StorePath).Read()["song"].Written);
    }

    [Fact]
    public void A_song_in_the_middle_band_writes_nothing_until_the_user_answers()
    {
        var store = new FamiliarityStore(StorePath);

        store.Measure(
            "song",
            new FamiliarityReading("Creepy Nuts", 48_382, 300_000),
            DateTimeOffset.UtcNow
        );

        Assert.Null(store.Read()["song"].Written);
    }

    [Fact]
    public void Settling_anything_but_the_two_values_is_refused() =>
        Assert.Throws<ArgumentException>(() =>
            new FamiliarityStore(StorePath).Settle("song", "Famous")
        );
}

public class FamiliarityLookupTests
{
    private sealed class Deezer(Dictionary<string, string> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.ToString();
            var body = answers
                .First(answer => url.Contains(answer.Key, StringComparison.Ordinal))
                .Value;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private const string Search = """
        {"data":[
          {"id":1,"title":"American Boy (Remix)","rank":100,"artist":{"name":"Someone Else"}},
          {"id":2,"title":"American Boy","rank":900317,"artist":{"name":"Estelle"}}
        ]}
        """;

    [Fact]
    public async Task The_loudest_credited_artist_counts_featured_ones_included()
    {
        var lookup = new FamiliarityLookup(
            new HttpClient(
                new Deezer(
                    new()
                    {
                        ["/search"] = Search,
                        ["/track/2"] =
                            """{"contributors":[{"id":10,"role":"Main"},{"id":11,"role":"Featured"}]}""",
                        ["/artist/10"] = """{"name":"Estelle","nb_fan":117646}""",
                        ["/artist/11"] = """{"name":"Kanye West","nb_fan":14000000}""",
                    }
                )
            )
        );

        var reading = await lookup.ReadAsync(
            "Estelle",
            "American Boy (feat. Kanye West)",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new FamiliarityReading("Kanye West", 14_000_000, 900_317), reading);
    }

    [Fact]
    public async Task A_song_deezer_does_not_have_reads_as_unknown_and_its_rate_limit_as_a_failure()
    {
        var missing = new FamiliarityLookup(
            new HttpClient(new Deezer(new() { ["/search"] = """{"data":[]}""" }))
        );
        var limited = new FamiliarityLookup(
            new HttpClient(
                new Deezer(new() { ["/search"] = """{"error":{"type":"Exception","code":4}}""" })
            )
        );

        Assert.Equal(
            FamiliarityReading.Unknown,
            await missing.ReadAsync("Nobody", "Nothing", TestContext.Current.CancellationToken)
        );
        Assert.Null(
            await limited.ReadAsync("Nobody", "Nothing", TestContext.Current.CancellationToken)
        );
    }
}
