using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crateside.Core.Tracks;

namespace Crateside.Core.Familiarity;

/// <summary>
/// Asks Deezer's public API, which needs no key, how far a song reaches: the song's own rank and
/// the fans of the most-followed artist credited on it, featured artists included (#52).
/// </summary>
public sealed partial class FamiliarityLookup(IHttpClientFactory clients)
{
    public const string ClientName = "familiarity-lookup";

    /// <summary>Raised whenever matching changes, so songs an older lookup missed are asked again.</summary>
    public const int Version = 3;

    private const int QuotaExceeded = 4;

    /// <summary>Deezer allows 50 requests in 5 seconds; this stays well under it.</summary>
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(150);

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Bracketed { get; }

    [GeneratedRegex(@"[^\p{L}\p{N}*]+")]
    private static partial Regex NotLetterOrDigit { get; }

    [GeneratedRegex(
        @"karaoke|tribute|originally\s+performed|made\s+famous|in\s+the\s+style\s+of|backing\s+track|\bcovered\s+by\b|\bcover\s+(version|band)\b|[\(\[][^\)\]]*\bcover\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ImitationMarker { get; }

    /// <summary>
    /// What Deezer knows, <see cref="FamiliarityReading.Unknown"/> when it does not know the song,
    /// or null when it could not be reached — which is worth asking again, where unknown is not.
    /// </summary>
    public async Task<FamiliarityReading?> ReadAsync(
        string artist,
        string title,
        CancellationToken ct = default
    )
    {
        try
        {
            using var search = await GetAsync(
                $"https://api.deezer.com/search?q={Uri.EscapeDataString($"{ArtistCredit.Primary(artist)} {Bracketed.Replace(title, string.Empty).Trim()}")}",
                ct
            );

            if (search is null)
                return null;

            if (Match(search.RootElement, artist, title) is not { } track)
                return FamiliarityReading.Unknown;

            await Task.Delay(Pace, ct);

            using var detail = await GetAsync($"https://api.deezer.com/track/{track.Id}", ct);

            if (detail is null)
                return null;

            (string Name, long Fans)? loudest = null;

            foreach (var contributor in Contributors(detail.RootElement))
            {
                await Task.Delay(Pace, ct);

                using var credited = await GetAsync(
                    $"https://api.deezer.com/artist/{contributor}",
                    ct
                );

                if (credited is null)
                    return null;

                if (
                    credited.RootElement.TryGetProperty("nb_fan", out var fans)
                    && credited.RootElement.TryGetProperty("name", out var name)
                    && fans.GetInt64() > (loudest?.Fans ?? -1)
                )
                    loudest = (name.GetString() ?? string.Empty, fans.GetInt64());
            }

            return new FamiliarityReading(loudest?.Name, loudest?.Fans, track.Rank);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException
                && !ct.IsCancellationRequested
            )
        {
            return null;
        }
    }

    /// <summary>
    /// The most popular hit that is this song by one of its artists, so the original outranks a
    /// live cut or a remaster of it. A hit by a karaoke, tribute or cover act is passed over for the
    /// next one.
    /// </summary>
    private static (long Id, int Rank)? Match(JsonElement search, string artist, string title) =>
        search.TryGetProperty("data", out var hits)
            ? hits.EnumerateArray()
                .Where(hit =>
                    hit.TryGetProperty("title", out var found)
                    && SameTitle(found.GetString() ?? string.Empty, title)
                    && hit.GetProperty("artist").GetProperty("name").GetString() is { } by
                    && ByOneOf(artist, by)
                    && !IsImitation(found.GetString() ?? string.Empty, by, title)
                )
                .Select(hit =>
                    ((long Id, int Rank)?)
                        (hit.GetProperty("id").GetInt64(), hit.GetProperty("rank").GetInt32())
                )
                .MaxBy(hit => hit!.Value.Rank)
            : null;

    /// <summary>
    /// The same title once both are squashed to letters and digits, one a prefix of the other, and
    /// with Deezer's censoring asterisks standing for any letter.
    /// </summary>
    internal static bool SameTitle(string found, string wanted)
    {
        var (f, w) = (Squash(found), Squash(wanted));

        if (f.Length is 0 || w.Length is 0)
            return false;

        if (!f.Contains('*'))
            return f.StartsWith(w, StringComparison.Ordinal)
                || w.StartsWith(f, StringComparison.Ordinal);

        return w.Length >= f.Length
            && f.Zip(w).All(pair => pair.First is '*' || pair.First == pair.Second);
    }

    /// <summary>
    /// Whether Deezer's artist is the credited one, or one of several credited, however either
    /// spells the name: the whole name, not a part of a longer one.
    /// </summary>
    internal static bool ByOneOf(string credited, string found) =>
        Squash(found) is { Length: > 0 } by
        && ArtistCredit.Split(credited).Append(credited).Any(one => Squash(one) == by);

    /// <summary>Whether the hit calls itself a karaoke, tribute or cover rendition the wanted song is not.</summary>
    internal static bool IsImitation(string foundTitle, string foundArtist, string wantedTitle) =>
        ImitationMarker.Match($"{foundTitle} | {foundArtist}")
            is { Success: true, Value: var marker }
        && !wantedTitle.Contains(marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lowercase letters, digits and <c>*</c> only, accents folded and <c>$</c> read as S.</summary>
    private static string Squash(string text)
    {
        var folded = Bracketed
            .Replace(text, string.Empty)
            .Replace('$', 's')
            .Replace("&", "and")
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) is not UnicodeCategory.NonSpacingMark)
            .ToArray();

        return NotLetterOrDigit.Replace(new string(folded), string.Empty).ToLowerInvariant();
    }

    private static IEnumerable<long> Contributors(JsonElement track) =>
        track.TryGetProperty("contributors", out var contributors)
            ? contributors.EnumerateArray().Select(c => c.GetProperty("id").GetInt64()).Distinct()
        : track.TryGetProperty("artist", out var artist) ? [artist.GetProperty("id").GetInt64()]
        : [];

    /// <summary>
    /// The body, or null when Deezer's rate limit refused it. Any other error body comes back as
    /// it is and reads as knowing nothing.
    /// </summary>
    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        using var response = await clients.CreateClient(ClientName).GetAsync(url, ct);

        response.EnsureSuccessStatusCode();

        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );

        if (
            document.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("error", out var error)
            || !error.TryGetProperty("code", out var code)
            || code.GetInt32() is not QuotaExceeded
        )
            return document;

        document.Dispose();

        return null;
    }
}
