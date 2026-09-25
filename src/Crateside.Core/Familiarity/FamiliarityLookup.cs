using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crateside.Core.Familiarity;

/// <summary>
/// Asks Deezer's public API, which needs no key, how far a song reaches: the song's own rank and
/// the fans of the most-followed artist credited on it, featured artists included (#52).
/// </summary>
public sealed partial class FamiliarityLookup(HttpClient client)
{
    private const int QuotaExceeded = 4;

    /// <summary>Deezer allows 50 requests in 5 seconds; this stays well under it.</summary>
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(150);

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Bracketed { get; }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord { get; }

    /// <summary>
    /// What Deezer knows, <see cref="FamiliarityReading.Unknown"/> when it does not know the song,
    /// or null when it could not be reached — which is worth asking again, where unknown is not.
    /// </summary>
    public async Task<FamiliarityReading?> ReadAsync(
        string artist,
        string title,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var search = await GetAsync(
                $"https://api.deezer.com/search?q={Uri.EscapeDataString($"{artist} {Plain(title)}")}",
                cancellationToken
            );

            if (search is null)
                return null;

            if (Match(search.RootElement, artist, title) is not { } track)
                return FamiliarityReading.Unknown;

            await Task.Delay(Pace, cancellationToken);

            using var detail = await GetAsync(
                $"https://api.deezer.com/track/{track.Id}",
                cancellationToken
            );

            if (detail is null)
                return null;

            (string Name, long Fans)? loudest = null;

            foreach (var contributor in Contributors(detail.RootElement))
            {
                await Task.Delay(Pace, cancellationToken);

                using var credited = await GetAsync(
                    $"https://api.deezer.com/artist/{contributor}",
                    cancellationToken
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
        catch (Exception e)
            when (e is HttpRequestException or TaskCanceledException or JsonException
                && !cancellationToken.IsCancellationRequested
            )
        {
            return null;
        }
    }

    /// <summary>The first hit whose title is the song's and whose artist shares a word with it.</summary>
    private static (long Id, int Rank)? Match(JsonElement search, string artist, string title)
    {
        if (!search.TryGetProperty("data", out var hits))
            return null;

        var wanted = Plain(title);

        foreach (var hit in hits.EnumerateArray())
        {
            if (
                hit.TryGetProperty("title", out var found)
                && Plain(found.GetString() ?? string.Empty) is var candidate
                && (candidate == wanted || candidate.Contains(wanted, StringComparison.Ordinal))
                && hit.GetProperty("artist").GetProperty("name").GetString() is { } by
                && SharesAWord(artist, by)
            )
                return (hit.GetProperty("id").GetInt64(), hit.GetProperty("rank").GetInt32());
        }

        return null;
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
    private async Task<JsonDocument?> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);

        response.EnsureSuccessStatusCode();

        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
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

    /// <summary>Lowercase words only, with any bracketed part such as (feat. …) dropped.</summary>
    private static string Plain(string text) =>
        NotWord.Replace(Bracketed.Replace(text, string.Empty), " ").Trim().ToLowerInvariant();

    private static bool SharesAWord(string asked, string found) =>
        Plain(asked)
            .Split(' ')
            .Where(word => word.Length > 1)
            .Intersect(Plain(found).Split(' '))
            .Any();
}
