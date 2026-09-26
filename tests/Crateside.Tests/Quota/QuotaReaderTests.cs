using System.Globalization;
using System.Net;
using System.Text.Json;
using Crateside.Core.Quota;

namespace Crateside.Tests.Quota;

/// <summary>Reading an undocumented meter: every failure has to end as unknown, not a throw.</summary>
public class QuotaReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public QuotaReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string CredentialsWith(string token)
    {
        var path = Path.Combine(_root, ".credentials.json");

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = token } })
        );

        return path;
    }

    // A real response from 2026-09-03, trimmed. limits carries a different number for the same
    // window (23 against 22.0), so it is a fallback and not a synonym.
    private const string RealisticPayload = """
        {
          "five_hour": {
            "utilization": 22.0,
            "resets_at": "2026-09-03T19:49:59.789047-05:00",
            "limit_dollars": null, "used_dollars": null, "locked_reason": null
          },
          "seven_day": { "utilization": 25.0, "resets_at": "2026-09-09T04:00:00-05:00" },
          "limits": [
            { "kind": "session", "group": "session", "percent": 23, "is_active": false },
            { "kind": "weekly_all", "group": "weekly", "percent": 25, "is_active": true }
          ],
          "member_dashboard_available": true,
          "tangelo": null, "iguana_necktie": null, "juniper_tide": null
        }
        """;

    [Fact]
    public async Task Reads_the_five_hour_window_out_of_a_realistic_payload()
    {
        var reader = Reader(HttpStatusCode.OK, RealisticPayload, out _);

        var quota = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(quota.IsKnown);

        Assert.Equal(22.0, quota.Utilization);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-03T19:49:59.789047-05:00", CultureInfo.InvariantCulture),
            quota.ResetsAt
        );
        Assert.Equal(4, quota.ResetsAt!.Value.ToUniversalTime().Day);
    }

    [Fact]
    public void Falls_back_to_the_session_row_of_limits_when_utilization_is_renamed()
    {
        // The kind tag keeps the weekly window, in the same array, from being read as this one.
        var quota = QuotaReader.Parse(
            """
            {
              "five_hour": { "percent": 22.0 },
              "limits": [
                { "kind": "weekly_all", "percent": 25 },
                { "kind": "session", "percent": 23, "resets_at": "2026-09-03T19:49:59-05:00" }
              ]
            }
            """
        );

        Assert.Equal(23, quota.Utilization);
        Assert.NotNull(quota.ResetsAt);
    }

    [Fact]
    public async Task Sends_the_token_and_the_beta_opt_in()
    {
        var reader = Reader(HttpStatusCode.OK, RealisticPayload, out var handler);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", handler.Sent?.Headers.Authorization?.Scheme);
        Assert.Equal("token-value", handler.Sent?.Headers.Authorization?.Parameter);
        Assert.Contains("oauth-2025-04-20", handler.Sent?.Headers.GetValues("anthropic-beta")!);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RealisticPayload)]
    [InlineData(HttpStatusCode.InternalServerError, RealisticPayload)]
    [InlineData(HttpStatusCode.OK, "not json at all")]
    [InlineData(HttpStatusCode.OK, """{"five_hour":{}}""")]
    [InlineData(HttpStatusCode.OK, """{"five_hour":{"percent":43}}""")]
    [InlineData(HttpStatusCode.OK, """{"limits":[{"percent":43}]}""")]
    public async Task Anything_it_cannot_read_is_unknown_rather_than_an_error(
        HttpStatusCode status,
        string payload
    )
    {
        // The last two carry the renamed field with no kind tag to recover it from.
        var quota = await Reader(status, payload, out _)
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(quota.IsKnown);
        Assert.Null(quota.Utilization);
    }

    [Fact]
    public async Task No_credentials_file_means_unknown_and_no_request()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, RealisticPayload);

        var reader = new QuotaReader(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") }
        )
        {
            CredentialsPath = Path.Combine(_root, "nothing-here.json"),
        };

        Assert.False((await reader.ReadAsync(TestContext.Current.CancellationToken)).IsKnown);
        Assert.Null(handler.Sent);
    }

    private QuotaReader Reader(HttpStatusCode status, string payload, out CapturingHandler handler)
    {
        handler = new CapturingHandler(status, payload);

        return new QuotaReader(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") }
        )
        {
            CredentialsPath = CredentialsWith("token-value"),
        };
    }

    private sealed class CapturingHandler(HttpStatusCode status, string payload)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Sent = request;

            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(payload) }
            );
        }
    }
}
