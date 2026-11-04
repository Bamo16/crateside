using System.Net;
using Crateside.Core.Orchestration;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration;

public sealed class SourceAvailabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly UnavailableSources _store = new();
    private readonly Dictionary<string, HttpStatusCode> _answers = [];
    private readonly List<string> _asked = [];

    private SourceAvailability Availability() =>
        new(new Clients(this), _store, TestLedger.Store(), _time);

    [Theory]
    [InlineData(HttpStatusCode.OK, "Available")]
    [InlineData(HttpStatusCode.NotFound, "Unavailable Removed")]
    [InlineData(HttpStatusCode.Unauthorized, "Unavailable Private")]
    [InlineData(HttpStatusCode.Forbidden, "Unavailable Private")]
    [InlineData(HttpStatusCode.BadRequest, "Unknown")]
    [InlineData(HttpStatusCode.TooManyRequests, "Unknown")]
    [InlineData(HttpStatusCode.InternalServerError, "Unknown")]
    public void A_status_maps_to_what_it_says_about_the_video(
        HttpStatusCode status,
        string expected
    ) =>
        Assert.Equal(
            expected,
            SourceAvailability.StatusOf(status) switch
            {
                VideoStatus.Available => "Available",
                VideoStatus.Unavailable(var reason) => $"Unavailable {reason}",
                _ => "Unknown",
            }
        );

    [Fact]
    public async Task A_network_failure_is_unknown_and_excludes_nothing()
    {
        _answers["aaaaaaaaaaa"] = HttpStatusCode.RequestTimeout;

        var report = await Availability()
            .SplitAsync(["aaaaaaaaaaa"], ct: TestContext.Current.CancellationToken);

        Assert.Equal(["aaaaaaaaaaa"], report.Downloadable);
        Assert.Empty(report.Unavailable);
        Assert.Empty(_store.All());
    }

    [Fact]
    public async Task An_unavailable_video_leaves_the_downloads_and_is_reported_with_its_reason()
    {
        _answers["gone0000000"] = HttpStatusCode.NotFound;
        _answers["here0000000"] = HttpStatusCode.OK;

        var report = await Availability()
            .SplitAsync(["gone0000000", "here0000000"], ct: TestContext.Current.CancellationToken);

        Assert.Equal(["here0000000"], report.Downloadable);

        var gone = Assert.Single(report.Unavailable);

        Assert.Equal("gone0000000", gone.TrackKey);
        Assert.Equal(UnavailableReason.Removed, gone.Reason);
        Assert.Equal(Start, gone.FirstSeen);
    }

    [Fact]
    public async Task A_video_known_unavailable_is_not_asked_about_again_within_a_month()
    {
        _answers["gone0000000"] = HttpStatusCode.NotFound;
        var availability = Availability();
        await availability.SplitAsync(["gone0000000"], ct: TestContext.Current.CancellationToken);
        _asked.Clear();

        _time.Advance(TimeSpan.FromDays(29));
        var report = await availability.SplitAsync(
            ["gone0000000"],
            ct: TestContext.Current.CancellationToken
        );

        Assert.Empty(_asked);
        Assert.Single(report.Unavailable);
    }

    [Fact]
    public async Task A_video_known_unavailable_is_asked_about_again_after_a_month_and_keeps_its_first_date()
    {
        _answers["gone0000000"] = HttpStatusCode.NotFound;
        var availability = Availability();
        await availability.SplitAsync(["gone0000000"], ct: TestContext.Current.CancellationToken);
        _asked.Clear();

        _time.Advance(TimeSpan.FromDays(32));
        var report = await availability.SplitAsync(
            ["gone0000000"],
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(["gone0000000"], _asked);
        Assert.Equal(Start, Assert.Single(report.Unavailable).FirstSeen);
    }

    [Fact]
    public async Task Checking_again_asks_about_every_unavailable_video_at_once()
    {
        _answers["gone0000000"] = HttpStatusCode.NotFound;
        var availability = Availability();
        await availability.SplitAsync(["gone0000000"], ct: TestContext.Current.CancellationToken);
        _asked.Clear();

        await availability.SplitAsync(
            ["gone0000000"],
            recheckAll: true,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(["gone0000000"], _asked);
    }

    [Fact]
    public async Task A_video_that_comes_back_is_forgotten_and_counts_as_missing_again()
    {
        _answers["back0000000"] = HttpStatusCode.NotFound;
        var availability = Availability();
        await availability.SplitAsync(["back0000000"], ct: TestContext.Current.CancellationToken);

        _answers["back0000000"] = HttpStatusCode.OK;
        var report = await availability.SplitAsync(
            ["back0000000"],
            recheckAll: true,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(["back0000000"], report.Downloadable);
        Assert.Empty(report.Unavailable);
        Assert.Empty(_store.All());
    }

    [Fact]
    public async Task What_is_known_survives_a_restart()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "crateside-tests",
            Guid.NewGuid().ToString("n"),
            "unavailable.json"
        );

        try
        {
            new UnavailableSources(path).Mark("gone0000000", UnavailableReason.Private, Start);

            var entry = Assert.Single(new UnavailableSources(path).All());

            Assert.Equal("gone0000000", entry.Key);
            Assert.Equal(UnavailableReason.Private, entry.Value.Reason);
            Assert.Equal(Start, entry.Value.FirstSeen);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private sealed class Clients(SourceAvailabilityTests owner) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(owner));
    }

    private sealed class Handler(SourceAvailabilityTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var trackKey = request.RequestUri!.Query.Split("watch?v=")[1].Split('&')[0];

            lock (owner._asked)
            {
                owner._asked.Add(trackKey);
            }

            return owner._answers.GetValueOrDefault(trackKey) is HttpStatusCode.RequestTimeout
                ? throw new HttpRequestException("timed out")
                : Task.FromResult(
                    new HttpResponseMessage(
                        owner._answers.GetValueOrDefault(trackKey, HttpStatusCode.OK)
                    )
                );
        }
    }
}
