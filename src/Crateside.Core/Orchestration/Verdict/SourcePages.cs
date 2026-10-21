using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Looks a song up on the sites where the human evidence is, before the judge runs (#112).</summary>
public interface ISourcePages
{
    Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        CancellationToken ct = default
    );
}

/// <summary>
/// Hooktheory, Musicnotes and Ultimate Guitar by artist and title: one request a site, two for
/// Musicnotes, whose search carries no key.
/// </summary>
public sealed partial class SourcePages(IHttpClientFactory clients) : ISourcePages
{
    public const string ClientName = "source-pages";

    private const int UltimateGuitarTabs = 3;
    private const int HooktheoryPagesPerSong = 4;
    private const string HooktheoryTabs = "https://www.hooktheory.com/theorytab/view/";
    private const string HooktheorySearchScript =
        "https://www.hooktheory.com/js/meilisearch/meilisearchAppDashboard.js";
    private const string HooktheorySearchUrl =
        "https://www.hooktheory.com/meili/indexes/theorytabs/search";

    public async Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        CancellationToken ct = default
    ) =>
        [
            await LookUpAsync("Hooktheory", () => HooktheoryAsync(artist, title, ct)),
            await LookUpAsync("Musicnotes", () => MusicnotesAsync(artist, title, ct)),
            await LookUpAsync("Ultimate Guitar", () => UltimateGuitarAsync(artist, title, ct)),
        ];

    /// <summary><c>Beyoncé</c> → <c>beyonce</c>, <c>Don't Stop Me Now</c> → <c>dont-stop-me-now</c>.</summary>
    public static string Slug(string text)
    {
        var plain = new StringBuilder();

        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) is not UnicodeCategory.NonSpacingMark)
                plain.Append(c);
        }

        return NotSlug
            .Replace(Apostrophes.Replace(plain.ToString().ToLowerInvariant(), string.Empty), "-")
            .Trim('-');
    }

    private static async Task<SiteLookup> LookUpAsync(
        string site,
        Func<Task<IReadOnlyList<FetchedPage>>> fetch
    )
    {
        try
        {
            return await fetch() is { Count: > 0 } pages
                ? new(site, LookupStatus.Found, pages)
                : new(site, LookupStatus.NotFound, []);
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        {
                            CancellationToken.IsCancellationRequested: false
                        }
            )
        {
            return new(site, LookupStatus.Unreachable, []) { Error = ex.Message };
        }
    }

    private async Task<IReadOnlyList<FetchedPage>> HooktheoryAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        List<FetchedPage> pages = [];

        foreach (var url in await HooktheoryUrlsAsync(artist, title, ct))
        {
            if (await GetAsync(url, ct) is not { } page)
                continue;

            Dictionary<string, string> scores = [];

            foreach (var section in HooktheoryPageReader.Read(page)?.Sections ?? [])
            {
                if (await SectionScoreAsync(section.Hash!, ct) is { } body)
                    scores[section.Hash!] = body;
            }

            pages.Add(HooktheoryPage(url, page, scores));
        }

        return pages;
    }

    /// <summary>The slug the artist and title spell, then every other TheoryTab the site's search files under them.</summary>
    private async Task<IReadOnlyList<string>> HooktheoryUrlsAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        List<string> urls = [$"{HooktheoryTabs}{Slug(artist)}/{Slug(title)}"];

        foreach (var found in await HooktheorySearchAsync(artist, title, ct))
        {
            if (!urls.Contains(found, StringComparer.OrdinalIgnoreCase))
                urls.Add(found);
        }

        return [.. urls.Take(HooktheoryPagesPerSong)];
    }

    private async Task<IReadOnlyList<string>> HooktheorySearchAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        try
        {
            if (
                await GetAsync(HooktheorySearchScript, ct) is not { } script
                || HooktheorySearchKey.Match(script).Groups["Key"]
                    is not { Success: true, Value: var key }
            )
                return [];

            using var request = new HttpRequestMessage(HttpMethod.Post, HooktheorySearchUrl)
            {
                Content = JsonContent.Create(
                    new HooktheorySearchQuery($"{artist} {title}"),
                    HooktheoryJsonContext.Default.HooktheorySearchQuery
                ),
            };

            request.Headers.Authorization = new("Bearer", key);

            using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);

            return response.IsSuccessStatusCode
                ? HooktheoryTabUrls(await response.Content.ReadAsStringAsync(ct), artist, title)
                : [];
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        {
                            CancellationToken.IsCancellationRequested: false
                        }
            )
        {
            return [];
        }
    }

    /// <summary>The tabs the search lists for this song under this artist, or the same artist with a feature credit, in the order it ranks them.</summary>
    internal static IReadOnlyList<string> HooktheoryTabUrls(
        string search,
        string artist,
        string title
    )
    {
        try
        {
            return
            [
                .. (
                    JsonSerializer
                        .Deserialize(search, HooktheoryJsonContext.Default.HooktheorySearchResponse)
                        ?.Hits
                    ?? []
                )
                    .Where(hit =>
                        hit
                            is {
                                Artist.Length: > 0,
                                Song.Length: > 0,
                                ArtistSlug.Length: > 0,
                                SongSlug.Length: > 0,
                            }
                        && Slug(hit.Song) == Slug(title)
                        && SameArtist(Slug(hit.Artist), Slug(artist))
                    )
                    .Select(hit =>
                        $"{HooktheoryTabs}{Uri.EscapeDataString(Uri.UnescapeDataString(hit.ArtistSlug!))}/{Uri.EscapeDataString(Uri.UnescapeDataString(hit.SongSlug!))}"
                    )
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The same slug, one with a credit added (<c>disclosure</c> and <c>disclosure-ft-sam-smith</c>), or spelled without hyphens (<c>billyjoel</c>).</summary>
    private static bool SameArtist(string hit, string wanted) =>
        hit == wanted
        || hit.StartsWith($"{wanted}-", StringComparison.Ordinal)
        || wanted.StartsWith($"{hit}-", StringComparison.Ordinal)
        || hit.Replace("-", string.Empty) == wanted.Replace("-", string.Empty);

    internal static FetchedPage HooktheoryPage(
        string url,
        string page,
        IReadOnlyDictionary<string, string>? scores = null
    )
    {
        var song = HooktheoryPageReader.Read(page);

        return new()
        {
            Site = "Hooktheory",
            Url = url,
            Heading = PageTitle(page),
            Keys =
            [
                .. (song?.Sections ?? []).Select(section => new PageKey(
                    $"{section.Key} {section.Scale}",
                    section.Name,
                    section.Tempo
                )),
            ],
            Tab = HooktheoryPageReader.Tab(song, scores),
        };
    }

    /// <summary>A section's score from the unauthenticated API, or null when it did not answer.</summary>
    private async Task<string?> SectionScoreAsync(string hash, CancellationToken ct)
    {
        try
        {
            return await GetAsync(
                $"https://api.hooktheory.com/v1/songs/public/{hash}?fields=ID,xmlData,song,jsonData",
                ct
            );
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        {
                            CancellationToken.IsCancellationRequested: false
                        }
            )
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<FetchedPage>> MusicnotesAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        var search =
            $"https://www.musicnotes.com/search/go?w={Uri.EscapeDataString($"{artist} {title}")}";

        if (
            await GetAsync(search, ct) is not { } results
            || MusicnotesProduct(results, artist, title) is not { } product
            || await GetAsync(product, ct) is not { } page
        )
            return [];

        return [MusicnotesPage(product, page)];
    }

    /// <summary>The first product filed under the artist and title themselves: the original artist's, not a cover's.</summary>
    internal static string? MusicnotesProduct(string results, string artist, string title) =>
        MusicnotesLink
            .Matches(results)
            .FirstOrDefault(link =>
                link.Groups["Artist"].Value == Slug(artist)
                && link.Groups["Title"].Value == Slug(title)
            )
            is { } found
            ? $"https://www.musicnotes.com{found.Value}"
            : null;

    internal static FetchedPage MusicnotesPage(string url, string page) =>
        new()
        {
            Site = "Musicnotes",
            Url = url,
            Heading = PageTitle(page),
            Arrangement = MusicnotesScoring.Match(page).Groups["Scoring"]
                is { Success: true, Value: var scoring }
                ? scoring
                : null,
            Note = MusicnotesArranger.Match(page).Groups["Name"]
                is { Success: true, Value: var arranger }
                ? $"user arrangement by {WebUtility.HtmlDecode(arranger)}"
                : null,
            Keys = MusicnotesKey.Match(page).Groups["Key"] is { Success: true, Value: var key }
                ? [new PageKey(key.Trim(), "Original Published Key", null)]
                : [],
        };

    private async Task<IReadOnlyList<FetchedPage>> UltimateGuitarAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        var search =
            $"https://www.ultimate-guitar.com/search.php?search_type=title&value={Uri.EscapeDataString($"{artist} {title}")}";

        return await GetAsync(search, ct) is { } page
            ? UltimateGuitarPages(page, artist, title)
            : [];
    }

    /// <summary>The song's most-voted chord charts, and every tab of it that states a key.</summary>
    internal static IReadOnlyList<FetchedPage> UltimateGuitarPages(
        string page,
        string artist,
        string title
    )
    {
        if (
            UltimateGuitarStore.Match(page).Groups["Json"] is not { Success: true, Value: var json }
        )
            return [];

        UltimateGuitarStoreDto? store;

        try
        {
            store = JsonSerializer.Deserialize(
                WebUtility.HtmlDecode(json),
                SourcePagesJsonContext.Default.UltimateGuitarStoreDto
            );
        }
        catch (JsonException)
        {
            return [];
        }

        return
        [
            .. (store?.Store?.Page?.Data?.Results ?? [])
                .Where(tab =>
                    tab is { TabUrl.Length: > 0, ArtistName: { } tabArtist, SongName: { } song }
                    && Slug(tabArtist) == Slug(artist)
                    && Slug(song) == Slug(title)
                    && (tab.Type is "Chords" || tab is { TonalityName.Length: > 0 })
                )
                .OrderByDescending(tab => tab.TonalityName is { Length: > 0 })
                .ThenByDescending(tab => tab.Votes ?? 0)
                .Take(UltimateGuitarTabs)
                .Select(tab => new FetchedPage
                {
                    Site = "Ultimate Guitar",
                    Url = tab.TabUrl!,
                    Heading = $"{tab.SongName} by {tab.ArtistName}",
                    Arrangement = tab.Type,
                    Keys = tab.TonalityName is { Length: > 0 } key
                        ? [new PageKey(key, null, null)]
                        : [],
                    Note = tab.Rating is { } rating
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"rated {rating:0.0} by {tab.Votes ?? 0}"
                        )
                        : null,
                }),
        ];
    }

    /// <summary>The page, or null for a page that is not there.</summary>
    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var response = await clients.CreateClient(ClientName).GetAsync(url, ct);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string? PageTitle(string page) =>
        TitleTag.Match(page).Groups["Title"] is { Success: true, Value: var title }
            ? WebUtility.HtmlDecode(title).Trim()
            : null;

    [GeneratedRegex(@"searchKey:\s*""(?<Key>[^""]+)""")]
    private static partial Regex HooktheorySearchKey { get; }

    [GeneratedRegex(@"/sheetmusic/(?<Artist>[^""/]+)/(?<Title>[^""/]+)/MN\d+")]
    private static partial Regex MusicnotesLink { get; }

    [GeneratedRegex(
        @"Original Published Key:?\s*(</?[a-z0-9]+[^>]*>\s*)*(?<Key>[^<\s][^<]*)",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex MusicnotesKey { get; }

    [GeneratedRegex(@"DC\.Contributor\.namePersonal"" content=""(?<Name>[^""]+)""")]
    private static partial Regex MusicnotesArranger { get; }

    [GeneratedRegex(@"""scoring"":""(?<Scoring>[^""]+)""")]
    private static partial Regex MusicnotesScoring { get; }

    [GeneratedRegex(@"class=""js-store"" data-content=""(?<Json>[^""]*)""")]
    private static partial Regex UltimateGuitarStore { get; }

    [GeneratedRegex(@"<title>(?<Title>[^<]*)</title>")]
    private static partial Regex TitleTag { get; }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotSlug { get; }

    [GeneratedRegex(@"['’]")]
    private static partial Regex Apostrophes { get; }
}

/// <summary>What one site gave for a song.</summary>
public sealed record SiteLookup(string Site, LookupStatus Status, IReadOnlyList<FetchedPage> Pages)
{
    public string? Error { get; init; }
}

public enum LookupStatus
{
    Found,
    NotFound,
    Unreachable,
}

/// <summary>A page found by artist and title; whether it is this recording is the judge's call.</summary>
public sealed record FetchedPage
{
    public required string Site { get; init; }
    public required string Url { get; init; }

    /// <summary>What the page calls itself, so the judge can tell whether it is this song.</summary>
    public string? Heading { get; init; }

    public string? Arrangement { get; init; }
    public IReadOnlyList<PageKey> Keys { get; init; } = [];
    public string? Note { get; init; }

    /// <summary>A TheoryTab's contributors, sections and sync, where the page is one.</summary>
    public HooktheoryTab? Tab { get; init; }
}

/// <summary>A key as the page writes it, with the section it covers where the page says.</summary>
public sealed record PageKey(string Key, string? Section, double? Tempo);

internal sealed record UltimateGuitarStoreDto
{
    public StoreDto? Store { get; init; }

    internal sealed record StoreDto(PageDto? Page);

    internal sealed record PageDto(DataDto? Data);

    internal sealed record DataDto(IReadOnlyList<TabDto>? Results);

    internal sealed record TabDto
    {
        public string? SongName { get; init; }
        public string? ArtistName { get; init; }
        public string? Type { get; init; }
        public string? TonalityName { get; init; }
        public double? Rating { get; init; }
        public int? Votes { get; init; }
        public string? TabUrl { get; init; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UltimateGuitarStoreDto))]
internal sealed partial class SourcePagesJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(IReadOnlyList<SiteLookup>))]
internal sealed partial class SiteLookupJsonContext : JsonSerializerContext { }
