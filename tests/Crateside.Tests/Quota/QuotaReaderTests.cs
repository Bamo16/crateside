using System.Globalization;
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

    // Copied from a real response on 2026-09-03, trimmed. Utilization is a percentage on 0-100 --
    // 22.0 means 22% -- resets_at is ISO-8601 with an offset, and limits carries a *different*
    // number for the same window (23 against 22.0), which is why it is a fallback and not a
    // synonym. The null codename keys are real and must not get in the way.
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

        // 22.0 meaning 22%, taken from five_hour rather than the 23 sitting in limits.
        Assert.Equal(22.0, quota.Utilization);
        // Round-tripped exactly, offset and all: the real value carries microseconds, and the
        // point of the assertion is that the -05:00 is honoured rather than dropped.
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-03T19:49:59.789047-05:00", CultureInfo.InvariantCulture),
            quota.ResetsAt
        );
        Assert.Equal(4, quota.ResetsAt!.Value.ToUniversalTime().Day);
    }

    [Fact]
    public void Falls_back_to_the_session_row_of_limits_when_utilization_is_renamed()
    {
        // The drift that already happened once, happening again: the tool this was learned from
        // parses `percent`, and `percent` still lives in limits. The kind tag is what makes the
        // fallback safe, since the weekly window is in the same array and reading it as the
        // five-hour one would be worse than showing nothing.
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
        // The last two are the renamed field with nothing to recover it from: five_hour carrying
        // only `percent`, and a limits array whose row is untagged. The fallback needs
        // kind: "session" to know which window it is looking at, and guessing between the
        // five-hour and the weekly one would be worse than saying nothing.
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
