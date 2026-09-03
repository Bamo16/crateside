using System.Net.Http.Headers;
using System.Text.Json;

namespace Crateside.Core.Quota;

/// <summary>
/// Reads the user's five-hour Claude window from Anthropic's usage endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Judging is the only stage that spends something metered, at about 5.3% of a window a track, so
/// the window is roughly eighteen tracks (ADR 0006). That is worth showing, because the number
/// governs whether starting a batch now is reasonable and there is no other way to see it.
/// </para>
/// <para>
/// <strong>The endpoint is undocumented and already drifting.</strong> The tool this was learned
/// from parses <c>percent</c>; the live response calls it <c>utilization</c> and carries
/// <c>percent</c> only in a parallel <c>limits</c> array, and half the top-level keys are internal
/// codenames. So every read here is defensive and every failure is <em>unknown</em> rather than an
/// error: a meter that cannot be read is a meter that says nothing, never a page that breaks.
/// </para>
/// <para>
/// <strong>The token is a live credential.</strong> It is read at the moment of the call, sent
/// only to Anthropic, and never stored on this object, logged, or put anywhere a UI could render
/// it. <see cref="QuotaSnapshot"/> deliberately carries no error text for the same reason — there
/// is no message for a credential to leak into.
/// </para>
/// </remarks>
public sealed class QuotaReader(HttpClient client)
{
    private const string UsagePath = "/api/oauth/usage";

    /// <summary>The beta opt-in the endpoint requires. Part of the contract, not of transport.</summary>
    private const string BetaHeader = "oauth-2025-04-20";

    /// <summary>
    /// Where Claude Code keeps its credentials. Settable so a test needs no real one.
    /// </summary>
    /// <remarks>
    /// An init property rather than a constructor parameter, so the only thing the container has
    /// to supply is the <see cref="HttpClient"/> — which is what lets this be registered as a
    /// typed client and get a rotated handler for free.
    /// </remarks>
    public string CredentialsPath { get; init; } = DefaultCredentialsPath();

    /// <summary>
    /// The current window, or <see cref="QuotaSnapshot.Unknown"/> if it cannot be read.
    /// </summary>
    /// <remarks>Never throws. Every failure is the same answer: nothing to show.</remarks>
    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (ReadAccessToken() is not { Length: > 0 } token)
            return QuotaSnapshot.Unknown;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsagePath);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", BetaHeader);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return QuotaSnapshot.Unknown;

            return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception e)
            when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return QuotaSnapshot.Unknown;
        }
    }

    /// <summary>
    /// Picks the two fields out of a payload whose shape is not promised.
    /// </summary>
    /// <remarks>
    /// Reads <c>five_hour</c> and nothing else, so a rename anywhere in the rest of the response
    /// costs nothing. A missing or malformed field yields unknown for that field alone.
    /// </remarks>
    internal static QuotaSnapshot Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("five_hour", out var window))
                return QuotaSnapshot.Unknown;

            return new QuotaSnapshot
            {
                Utilization =
                    window.TryGetProperty("utilization", out var used)
                    && used.ValueKind is JsonValueKind.Number
                        ? used.GetDouble()
                        : null,
                ResetsAt = ResetTimeOf(window),
            };
        }
        catch (JsonException)
        {
            return QuotaSnapshot.Unknown;
        }
    }

    /// <summary>
    /// When the window rolls over, spelled either way.
    /// </summary>
    /// <remarks>
    /// A string is read as ISO-8601 and a number as Unix seconds. Which one this endpoint actually
    /// sends has not been observed; accepting both costs three lines and removes the guess.
    /// </remarks>
    private static DateTimeOffset? ResetTimeOf(JsonElement window) =>
        window.TryGetProperty("resets_at", out var resets)
            ? resets.ValueKind switch
            {
                JsonValueKind.String when DateTimeOffset.TryParse(resets.GetString(), out var at) =>
                    at,
                JsonValueKind.Number => DateTimeOffset.FromUnixTimeSeconds(resets.GetInt64()),
                _ => null,
            }
            : null;

    /// <summary>
    /// The OAuth access token from the Claude Code credentials file.
    /// </summary>
    /// <remarks>
    /// Plaintext on Windows, so there is no keychain step. Returned rather than held: the value
    /// lives for the length of one request and this type keeps no field for it.
    /// </remarks>
    private string? ReadAccessToken()
    {
        try
        {
            if (!File.Exists(CredentialsPath))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(CredentialsPath));

            return
                document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                && oauth.TryGetProperty("accessToken", out var token)
                ? token.GetString()
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DefaultCredentialsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            ".credentials.json"
        );
}

/// <summary>
/// The five-hour window as last read, or nothing at all.
/// </summary>
/// <remarks>
/// Carries no error text on purpose. The only thing that can go wrong here involves a live
/// credential, and a snapshot with nowhere to put a message is a snapshot that cannot leak one.
/// </remarks>
public sealed record QuotaSnapshot
{
    /// <summary>How much of the window is spent, as the endpoint reports it.</summary>
    public double? Utilization { get; init; }

    /// <summary>When the window rolls over.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>Whether there is anything to show. False is an ordinary state, not a failure.</summary>
    public bool IsKnown => Utilization is not null;

    public static QuotaSnapshot Unknown { get; } = new();
}
