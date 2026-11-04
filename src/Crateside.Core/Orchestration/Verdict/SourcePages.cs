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

    /// <summary>As above, accepting a page filed under any of <paramref name="alsoCredited"/> as well as under the artist (ADR 0077).</summary>
    Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
        CancellationToken ct = default
    ) => FetchAsync(artist, title, ct);
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
    private const int HooktheoryCandidatesPerSong = 2;
    private const string HooktheoryTabs = "https://www.hooktheory.com/theorytab/view/";
    private const string HooktheorySearchScript =
        "https://www.hooktheory.com/js/meilisearch/meilisearchAppDashboard.js";
    private const string HooktheorySearchUrl =
        "https://www.hooktheory.com/meili/indexes/theorytabs/search";

    public Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        CancellationToken ct = default
    ) => FetchAsync(artist, title, [], ct);

    /// <summary>A bracketed tail on the title (<c>(feat. …)</c>, <c>(Remaster)</c>) or a <c> - Act 1</c> is left out, since the sites file the song without it.</summary>
    public async Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
        CancellationToken ct = default
    )
    {
        var bare = BareTitle(title);

        return
        [
            await LookUpAsync("Hooktheory", () => HooktheoryAsync(artist, bare, alsoCredited, ct)),
            await LookUpAsync(
                "Musicnotes",
                async () => new FoundPages(await MusicnotesAsync(artist, bare, ct), [])
            ),
            await LookUpAsync(
                "Ultimate Guitar",
                async () => new FoundPages(await UltimateGuitarAsync(artist, bare, ct), [])
            ),
        ];
    }

    internal static string BareTitle(string title) =>
        PartTail.Replace(BracketedTail.Replace(title, string.Empty), string.Empty).Trim()
            is { Length: > 0 } bare
            ? bare
            : title;

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

    private static async Task<SiteLookup> LookUpAsync(string site, Func<Task<FoundPages>> fetch)
    {
        try
        {
            var found = await fetch();

            return (
                found.Pages is { Count: > 0 } pages
                    ? new SiteLookup(site, LookupStatus.Found, pages)
                    : new SiteLookup(site, LookupStatus.NotFound, [])
            ) with
            {
                Candidates = found.Candidates is { Count: > 0 } ? found.Candidates : null,
            };
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

    private async Task<FoundPages> HooktheoryAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
        CancellationToken ct
    )
    {
        List<FetchedPage> pages = [];
        var hits = await HooktheorySearchAsync(artist, title, alsoCredited, ct);

        foreach (var url in HooktheoryUrls(artist, title, hits.Confirmed))
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

        List<FetchedPage> candidates = [];

        foreach (var candidate in hits.Candidates.Take(HooktheoryCandidatesPerSong))
        {
            if (await GetAsync(candidate.Url, ct) is { } page)
                candidates.Add(
                    HooktheoryPage(candidate.Url, page) with
                    {
                        Note =
                            $"unconfirmed: filed under {candidate.Artist}, who is none of the artists this track credits",
                    }
                );
        }

        return new(pages, candidates);
    }

    /// <summary>The slug the artist and title spell, then every other TheoryTab the search confirmed.</summary>
    private static IReadOnlyList<string> HooktheoryUrls(
        string artist,
        string title,
        IReadOnlyList<string> confirmed
    )
    {
        List<string> urls = Slug(artist) is { Length: > 0 } slug
            ? [$"{HooktheoryTabs}{slug}/{Slug(title)}"]
            : [];

        foreach (var found in confirmed)
        {
            if (!urls.Contains(found, StringComparer.OrdinalIgnoreCase))
                urls.Add(found);
        }

        return [.. urls.Take(HooktheoryPagesPerSong)];
    }

    /// <summary>Searches by artist and title, and by title alone, since a game's page is rarely filed under the artist a file credits.</summary>
    private async Task<HooktheoryHits> HooktheorySearchAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
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
                return HooktheoryHits.None;

            List<string> bodies = [];

            foreach (var query in new[] { $"{artist} {title}".Trim(), title }.Distinct())
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, HooktheorySearchUrl)
                {
                    Content = JsonContent.Create(
                        new HooktheorySearchQuery(query),
                        HooktheoryJsonContext.Default.HooktheorySearchQuery
                    ),
                };

                request.Headers.Authorization = new("Bearer", key);

                using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                    bodies.Add(await response.Content.ReadAsStringAsync(ct));
            }

            return HooktheoryHits.Merge(
                bodies.Select(body => HooktheoryTabUrls(body, [artist, .. alsoCredited], title))
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
            return HooktheoryHits.None;
        }
    }

    internal static IReadOnlyList<string> HooktheoryTabUrls(
        string search,
        string artist,
        string title
    ) => HooktheoryTabUrls(search, [artist], title).Confirmed;

    /// <summary>
    /// The tabs the search lists for this song under one of these artists (or the same with a
    /// feature credit), in the order it ranks them; and those with the exact title under nobody
    /// the track credits, which are only candidates.
    /// </summary>
    internal static HooktheoryHits HooktheoryTabUrls(
        string search,
        IReadOnlyList<string> artists,
        string title
    )
    {
        try
        {
            var titled = (
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
                )
                .Select(hit =>
                    (
                        Hit: hit,
                        Url: $"{HooktheoryTabs}{Uri.EscapeDataString(Uri.UnescapeDataString(hit.ArtistSlug!))}/{Uri.EscapeDataString(Uri.UnescapeDataString(hit.SongSlug!))}"
                    )
                )
                .DistinctBy(found => found.Url, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new(
                [
                    .. titled
                        .Where(found => IsCredited(found.Hit.Artist!, artists))
                        .Select(found => found.Url),
                ],
                [
                    .. titled
                        .Where(found => !IsCredited(found.Hit.Artist!, artists))
                        .Select(found => new HooktheoryCandidate(found.Url, found.Hit.Artist!)),
                ]
            );
        }
        catch (JsonException)
        {
            return HooktheoryHits.None;
        }
    }

    /// <summary>Whether a page's artist is any of the credited names, spelled loosely.</summary>
    internal static bool IsCredited(string pageArtist, IEnumerable<string> credited) =>
        credited
            .SelectMany(names => names.Split([',', ';', '&', '/'], StringSplitOptions.TrimEntries))
            .Where(name => name.Length > 0)
            .Any(name =>
                SameArtist(Slug(pageArtist), Slug(name)) || SameSurnameAndInitial(pageArtist, name)
            );

    /// <summary>Dave Wise and David Wise: the same last word and the same first letter of the first.</summary>
    internal static bool SameSurnameAndInitial(string a, string b) =>
        Words(a) is [var firstA, .., var lastA]
        && Words(b) is [var firstB, .., var lastB]
        && lastA == lastB
        && firstA[0] == firstB[0];

    private static string[] Words(string name) =>
        Slug(name).Split('-', StringSplitOptions.RemoveEmptyEntries);

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

    [GeneratedRegex(@"(\s*[(\[][^()\[\]]*[)\]])+\s*$")]
    private static partial Regex BracketedTail { get; }

    [GeneratedRegex(@"\s+-\s+(?:Act|Part)\s+(?:\d+|[IVX]+|[A-Za-z]+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PartTail { get; }

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

    /// <summary>Pages with the exact title under no artist the track credits: shown to the judge and the person, and never a source for a rule (ADR 0077).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FetchedPage>? Candidates { get; init; }
}

internal sealed record FoundPages(
    IReadOnlyList<FetchedPage> Pages,
    IReadOnlyList<FetchedPage> Candidates
);

internal sealed record HooktheoryCandidate(string Url, string Artist);

internal sealed record HooktheoryHits(
    IReadOnlyList<string> Confirmed,
    IReadOnlyList<HooktheoryCandidate> Candidates
)
{
    public static HooktheoryHits None { get; } = new([], []);

    /// <summary>Every search's hits in order, a page once, and a page one search confirmed never a candidate of another.</summary>
    public static HooktheoryHits Merge(IEnumerable<HooktheoryHits> searches)
    {
        var all = searches.ToList();
        var confirmed = all.SelectMany(hits => hits.Confirmed)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new(
            confirmed,
            [
                .. all.SelectMany(hits => hits.Candidates)
                    .Where(candidate =>
                        !confirmed.Contains(candidate.Url, StringComparer.OrdinalIgnoreCase)
                    )
                    .DistinctBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase),
            ]
        );
    }
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
