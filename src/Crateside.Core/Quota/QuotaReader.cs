using System.Net.Http.Headers;
using System.Text.Json;

namespace Crateside.Core.Quota;

/// <summary>Reads the user's Claude usage windows.</summary>
public interface IQuotaReader
{
    /// <summary>The current windows, or <see cref="QuotaSnapshot.Unknown"/> if they cannot be read.</summary>
    Task<QuotaSnapshot> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads the user's five-hour Claude window from Anthropic's usage endpoint, which is undocumented.
/// </summary>
public sealed class QuotaReader(IHttpClientFactory clients) : IQuotaReader
{
    public const string ClientName = "anthropic-usage";

    private const string UsagePath = "/api/oauth/usage";

    /// <summary>The beta opt-in the endpoint requires.</summary>
    private const string BetaHeader = "oauth-2025-04-20";

    /// <summary>Where Claude Code keeps its credentials. Settable so a test needs no real one.</summary>
    public string CredentialsPath { get; init; } = DefaultCredentialsPath();

    /// <summary>
    /// The current window, or <see cref="QuotaSnapshot.Unknown"/> — never an exception — if it
    /// cannot be read.
    /// </summary>
    public async Task<QuotaSnapshot> ReadAsync(CancellationToken ct = default)
    {
        if (ReadAccessToken() is not { Length: > 0 } token)
            return QuotaSnapshot.Unknown;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsagePath);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", BetaHeader);

            using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return QuotaSnapshot.Unknown;

            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return QuotaSnapshot.Unknown;
        }
    }

    /// <summary>
    /// Picks the windows out of a payload whose shape is not promised: <c>five_hour</c> first,
    /// then the <c>session</c> entry of <c>limits</c>; <c>seven_day</c>, then <c>weekly_all</c>.
    /// </summary>
    internal static QuotaSnapshot Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var session =
                root.TryGetProperty("five_hour", out var window)
                && Percentage(window, "utilization") is { } utilization
                    ? new QuotaSnapshot
                    {
                        Utilization = utilization,
                        ResetsAt = ResetTimeOf(window),
                    }
                    : Limit(root, "session");
            var weekly =
                root.TryGetProperty("seven_day", out var week)
                && Percentage(week, "utilization") is { } used
                    ? new QuotaSnapshot { Utilization = used, ResetsAt = ResetTimeOf(week) }
                    : Limit(root, "weekly_all");

            return session with
            {
                WeeklyUtilization = weekly.Utilization,
                WeeklyResetsAt = weekly.ResetsAt,
            };
        }
        catch (JsonException)
        {
            return QuotaSnapshot.Unknown;
        }
    }

    /// <summary>
    /// A window as <c>limits</c> spells it, if it is there. A fallback only: its <c>percent</c>
    /// is a coarser reading than <c>utilization</c>.
    /// </summary>
    private static QuotaSnapshot Limit(JsonElement root, string wanted)
    {
        if (
            !root.TryGetProperty("limits", out var limits)
            || limits.ValueKind is not JsonValueKind.Array
        )
            return QuotaSnapshot.Unknown;

        foreach (var limit in limits.EnumerateArray())
        {
            if (
                limit.TryGetProperty("kind", out var kind)
                && kind.ValueKind is JsonValueKind.String
                && kind.GetString() == wanted
                && Percentage(limit, "percent") is { } percent
            )
                return new QuotaSnapshot { Utilization = percent, ResetsAt = ResetTimeOf(limit) };
        }

        return QuotaSnapshot.Unknown;
    }

    /// <summary>A number on 0–100, or null where there is not one.</summary>
    private static double? Percentage(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number
            ? value.GetDouble()
            : null;

    /// <summary>When the window rolls over: ISO-8601 with an offset, or Unix seconds.</summary>
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
    /// The OAuth access token from the Claude Code credentials file. Plaintext on Windows.
    /// </summary>
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
        catch (Exception ex)
            when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DefaultCredentialsPath() =>
        Environment.SpecialFolder.UserProfile.GetFolderPath(".claude", ".credentials.json");
}

/// <summary>
/// The five-hour and weekly windows as last read, or nothing at all. Carries no error text on purpose,
/// since the only failure here involves a live credential.
/// </summary>
public sealed record QuotaSnapshot
{
    /// <summary>How much of the window is spent, as a percentage on 0–100 (22.0 meaning 22%).</summary>
    public double? Utilization { get; init; }

    /// <summary>When the window rolls over.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>How much of the weekly limit is spent, on the same scale as <see cref="Utilization"/>.</summary>
    public double? WeeklyUtilization { get; init; }

    public DateTimeOffset? WeeklyResetsAt { get; init; }

    /// <summary>Whether there is anything to show. False is an ordinary state, not a failure.</summary>
    public bool IsKnown => Utilization is not null;

    public static QuotaSnapshot Unknown { get; } = new();
}
