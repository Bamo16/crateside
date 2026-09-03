using System.Net;
using System.Text.Json;
using Crateside.Core.Quota;

namespace Crateside.Tests.Quota;

/// <summary>
/// Reading a meter nobody promised would stay the same shape.
/// </summary>
/// <remarks>
/// The endpoint is undocumented and has already drifted once — the tool this was learned from
/// parses <c>percent</c> and the live response says <c>utilization</c>. So these assert the
/// failure modes as much as the happy path: every one of them has to end as <em>unknown</em>
/// rather than as an exception reaching a page.
/// </remarks>
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

    // Shaped after the real response: the two fields that matter, a parallel limits array that
    // spells the same thing differently, and codename keys that must not get in the way.
    private const string RealisticPayload = """
        {
          "tangelo": { "enabled": true },
          "five_hour": { "utilization": 0.43, "resets_at": "2026-09-03T18:40:00+00:00" },
          "seven_day": { "utilization": 0.12 },
          "limits": [ { "name": "five_hour", "percent": 43 } ],
          "iguana_necktie": "whatever this is"
        }
        """;

    [Fact]
    public async Task Reads_the_five_hour_window_out_of_a_realistic_payload()
    {
        var reader = Reader(HttpStatusCode.OK, RealisticPayload, out _);

        var quota = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(quota.IsKnown);
        Assert.Equal(0.43, quota.Utilization);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 18, 40, 0, TimeSpan.Zero),
            quota.ResetsAt!.Value.ToUniversalTime()
        );
    }

    [Fact]
    public async Task Sends_the_token_and_the_beta_opt_in()
    {
        // Both are the contract with an undocumented endpoint. The authorization header is built
        // per request because the token is read at the moment of the call and never held.
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
        // The fifth and sixth cases are the drift that has already happened once, arriving again:
        // the field renamed, or moved into the parallel array. Neither may take the page down.
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
            CancellationToken cancellationToken
        )
        {
            Sent = request;

            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(payload) }
            );
        }
    }
}
