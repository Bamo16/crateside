using System.Globalization;
using System.Net;
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

    /// <summary>As above, also accepting a song the sites file with the album's game in its name (<c>Hot-Head Bop - Donkey Kong Country 2</c>).</summary>
    Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
        string? album,
        CancellationToken ct = default
    ) => FetchAsync(artist, title, alsoCredited, ct);

    /// <summary>One page by its address, read as a found page is; a refusal says why it is not one the rules can count (ADR 0078).</summary>
    Task<PageFetch> FetchPageAsync(string address, CancellationToken ct = default) =>
        Task.FromResult<PageFetch>(
            new PageFetch.Refused("This lookup cannot fetch a page by address.")
        );
}

/// <summary>
/// Hooktheory, Musicnotes and Ultimate Guitar by artist and title: one request a site, two for
/// Musicnotes, whose search carries no key.
/// </summary>
public sealed partial class SourcePages(IHttpClientFactory clients) : ISourcePages
{
    public const string ClientName = "source-pages";

    private const int MaxPassed = 20;
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
    public Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
        CancellationToken ct = default
    ) => FetchAsync(artist, title, alsoCredited, null, ct);

    public async Task<IReadOnlyList<SiteLookup>> FetchAsync(
        string artist,
        string title,
        IReadOnlyList<string> alsoCredited,
        string? album,
        CancellationToken ct = default
    )
    {
        var bare = BareTitle(title);

        return
        [
            await LookUpAsync(
                "Hooktheory",
                () => HooktheoryAsync(artist, bare, alsoCredited, album, ct)
            ),
            await LookUpAsync("Musicnotes", () => MusicnotesAsync(artist, bare, ct)),
            await LookUpAsync("Ultimate Guitar", () => UltimateGuitarAsync(artist, bare, ct)),
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
            .Replace(
                Apostrophes.Replace(
                    InitialismDots.Replace(plain.ToString().ToLowerInvariant(), string.Empty),
                    string.Empty
                ),
                "-"
            )
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
                Passed = found.Passed is { Count: > 0 } passed ? [.. passed.Take(MaxPassed)] : null,
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
        string? album,
        CancellationToken ct
    )
    {
        List<FetchedPage> pages = [];
        var hits = await HooktheorySearchAsync(artist, title, alsoCredited, album, ct);

        foreach (var url in HooktheoryUrls(artist, title, hits.Confirmed))
        {
            if (await HooktheoryTabAsync(url, ct) is { } tab)
                pages.Add(tab);
        }

        var read = HooktheoryUrls(artist, title, hits.Confirmed);
        List<PassedPage> passed =
        [
            .. hits
                .Confirmed.Where(url => !read.Contains(url, StringComparer.OrdinalIgnoreCase))
                .Select(url => new PassedPage(
                    url,
                    artist,
                    title,
                    $"also filed under this song, past the {HooktheoryPagesPerSong} pages read"
                )),
            .. hits
                .Candidates.Skip(HooktheoryCandidatesPerSong)
                .Select(candidate => new PassedPage(
                    candidate.Url,
                    candidate.Artist,
                    title,
                    $"the exact title under {candidate.Artist}, past the {HooktheoryCandidatesPerSong} candidates read"
                )),
            .. hits.Passed,
        ];
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

        return new(pages, candidates) { Passed = passed };
    }

    private async Task<FetchedPage?> HooktheoryTabAsync(string url, CancellationToken ct)
    {
        if (await GetAsync(url, ct) is not { } page)
            return null;

        Dictionary<string, string> scores = [];

        foreach (var section in HooktheoryPageReader.Read(page)?.Sections ?? [])
        {
            if (await SectionScoreAsync(section.Hash!, ct) is { } body)
                scores[section.Hash!] = body;
        }

        return HooktheoryPage(url, page, scores);
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
        string? album,
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
                bodies.Select(body =>
                    HooktheoryTabUrls(body, [artist, .. alsoCredited], title, album)
                )
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
        string title,
        string? album = null
    )
    {
        try
        {
            var listed = (
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
                )
                .Select(hit =>
                    (
                        Hit: hit,
                        Url: $"{HooktheoryTabs}{Uri.EscapeDataString(Uri.UnescapeDataString(hit.ArtistSlug!))}/{Uri.EscapeDataString(Uri.UnescapeDataString(hit.SongSlug!))}"
                    )
                )
                .DistinctBy(found => found.Url, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var titled = listed.Where(found => IsSong(found.Hit.Song!, title, album)).ToList();

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
            )
            {
                Passed =
                [
                    .. listed
                        .Except(titled)
                        .Select(found => new PassedPage(
                            found.Url,
                            found.Hit.Artist,
                            found.Hit.Song,
                            "a different title"
                        )),
                ],
            };
        }
        catch (JsonException)
        {
            return HooktheoryHits.None;
        }
    }

    /// <summary>
    /// Whether a page's song is this title, spelled loosely, or this title with the album's game
    /// named on one side of a <c> - </c> (<c>Hot-Head Bop - Donkey Kong Country 2</c> on
    /// <c>Donkey Kong Country 2: Diddy's Kong Quest</c>).
    /// </summary>
    internal static bool IsSong(string pageSong, string title, string? album) =>
        Slug(pageSong) == Slug(title)
        || (
            album?.Split([": ", " - "], 2, StringSplitOptions.None)[0] is { } game
            && Slug(game) is { Length: > 0 } gameSlug
            && pageSong.Split(" - ", StringSplitOptions.TrimEntries) is [var first, var second]
            && (
                (Slug(first) == Slug(title) && Slug(second) == gameSlug)
                || (Slug(second) == Slug(title) && Slug(first) == gameSlug)
            )
        );

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

    private async Task<FoundPages> MusicnotesAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        var search =
            $"https://www.musicnotes.com/search/go?w={Uri.EscapeDataString($"{artist} {title}")}";

        if (await GetAsync(search, ct) is not { } results)
            return new([], []);

        var product = MusicnotesProduct(results, artist, title);
        var read =
            product is not null && await GetAsync(product, ct) is { } page
                ? MusicnotesPage(product, page)
                : null;

        return new(read is null ? [] : [read], [])
        {
            Passed = [.. MusicnotesPassed(results, product)],
        };
    }

    /// <summary>The products the search listed other than the one read, each filed under another artist or title.</summary>
    internal static IEnumerable<PassedPage> MusicnotesPassed(string results, string? read) =>
        MusicnotesLink
            .Matches(results)
            .Select(link => (Url: $"https://www.musicnotes.com{link.Value}", Link: link))
            .Where(found => !found.Url.Equals(read, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(found => found.Url, StringComparer.OrdinalIgnoreCase)
            .Select(found => new PassedPage(
                found.Url,
                found.Link.Groups["Artist"].Value,
                found.Link.Groups["Title"].Value,
                "filed under another artist or title"
            ));

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

    private async Task<FoundPages> UltimateGuitarAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        var search =
            $"https://www.ultimate-guitar.com/search.php?search_type=title&value={Uri.EscapeDataString($"{artist} {title}")}";

        return await GetAsync(search, ct) is { } page
            ? UltimateGuitarFound(page, artist, title)
            : new([], []);
    }

    /// <summary>The song's most-voted chord charts, and every tab of it that states a key.</summary>
    internal static IReadOnlyList<FetchedPage> UltimateGuitarPages(
        string page,
        string artist,
        string title
    ) => UltimateGuitarFound(page, artist, title).Pages;

    internal static FoundPages UltimateGuitarFound(string page, string artist, string title)
    {
        if (
            UltimateGuitarStore.Match(page).Groups["Json"] is not { Success: true, Value: var json }
        )
            return new([], []);

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
            return new([], []);
        }

        var results = (store?.Store?.Page?.Data?.Results ?? [])
            .Where(tab => tab is { TabUrl.Length: > 0 })
            .ToList();
        var thisSong = results
            .Where(tab =>
                tab is { ArtistName: { } tabArtist, SongName: { } song }
                && Slug(tabArtist) == Slug(artist)
                && Slug(song) == Slug(title)
            )
            .ToList();
        var usable = thisSong
            .Where(tab => tab.Type is "Chords" || tab is { TonalityName.Length: > 0 })
            .OrderByDescending(tab => tab.TonalityName is { Length: > 0 })
            .ThenByDescending(tab => tab.Votes ?? 0)
            .ToList();
        var taken = usable.Take(UltimateGuitarTabs).ToList();

        return new(
            [
                .. taken.Select(tab => new FetchedPage
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
            ],
            []
        )
        {
            Passed =
            [
                .. results
                    .Except(taken)
                    .Select(tab => new PassedPage(
                        tab.TabUrl!,
                        tab.ArtistName,
                        tab.SongName,
                        !thisSong.Contains(tab) ? "another artist or title"
                            : usable.Contains(tab) ? $"past the {UltimateGuitarTabs} tabs read"
                            : "no chords and no key stated"
                    )),
            ],
        };
    }

    public async Task<PageFetch> FetchPageAsync(string address, CancellationToken ct = default)
    {
        if (CountedAddress(address) is not PageAddress.Counted(var site, var url))
            return new PageFetch.Refused(RefusalFor(address));

        try
        {
            var page =
                site is "Hooktheory" ? await HooktheoryTabAsync(url, ct)
                : await GetAsync(url, ct) is { } body ? MusicnotesPage(url, body)
                : null;

            return page switch
            {
                null => new PageFetch.Refused($"{site} has no page at that address."),
                { Keys: [] } => new PageFetch.Refused(
                    "That page states no key, so there is nothing in it for the rules to count."
                ),
                _ => new PageFetch.Found(page),
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
            return new PageFetch.Refused($"{site} could not be reached: {ex.Message}");
        }
    }

    /// <summary>The canonical address of a Hooktheory TheoryTab or a Musicnotes product, or neither.</summary>
    internal static PageAddress CountedAddress(string address)
    {
        var text = address.Trim();

        if (HooktheoryAddress.Match(text) is { Success: true } tab)
            return new PageAddress.Counted(
                "Hooktheory",
                $"{HooktheoryTabs}{Canonical(tab.Groups["Artist"].Value)}/{Canonical(tab.Groups["Song"].Value)}"
            );

        return MusicnotesAddress.Match(text) is { Success: true } product
            ? new PageAddress.Counted(
                "Musicnotes",
                $"https://www.musicnotes.com{product.Groups["Path"].Value}"
            )
            : new PageAddress.Other();
    }

    private static string Canonical(string segment) =>
        Uri.EscapeDataString(Uri.UnescapeDataString(segment));

    private static string RefusalFor(string address) =>
        UltimateGuitarAddress.IsMatch(address.Trim())
            ? "Ultimate Guitar pages are shown but never counted, so one cannot be added."
            : "Only a Hooktheory TheoryTab (hooktheory.com/theorytab/view/artist/song) or a Musicnotes product page (musicnotes.com/sheetmusic/artist/title/MN…) can be added.";

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

    [GeneratedRegex(
        @"^https?://(www\.)?hooktheory\.com/theorytab/view/(?<Artist>[^/?#\s]+)/(?<Song>[^/?#\s]+)/?([?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture
    )]
    private static partial Regex HooktheoryAddress { get; }

    [GeneratedRegex(
        @"^https?://(www\.)?musicnotes\.com(?<Path>/sheetmusic/[^/?#\s]+/[^/?#\s]+/MN\d+)/?([?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture
    )]
    private static partial Regex MusicnotesAddress { get; }

    [GeneratedRegex(@"^https?://([a-z]+\.)?ultimate-guitar\.com/", RegexOptions.IgnoreCase)]
    private static partial Regex UltimateGuitarAddress { get; }

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

    [GeneratedRegex(@"(?<=\b[a-z])\.")]
    private static partial Regex InitialismDots { get; }
}

/// <summary>A page fetched by its address, or why it was not.</summary>
public abstract record PageFetch
{
    private PageFetch() { }

    public sealed record Found(FetchedPage Page) : PageFetch;

    public sealed record Refused(string Message) : PageFetch;
}

internal abstract record PageAddress
{
    private PageAddress() { }

    public sealed record Counted(string Site, string Url) : PageAddress;

    public sealed record Other : PageAddress;
}

/// <summary>What one site gave for a song.</summary>
public sealed record SiteLookup(string Site, LookupStatus Status, IReadOnlyList<FetchedPage> Pages)
{
    public string? Error { get; init; }

    /// <summary>Pages the person said are not this song: left out of what counts, and kept so they can be taken back (ADR 0078).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FetchedPage>? Rejected { get; init; }

    /// <summary>Pages with the exact title under no artist the track credits: shown to the judge and the person, and never a source for a rule (ADR 0077).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FetchedPage>? Candidates { get; init; }

    /// <summary>
    /// The search's other hits, never fetched, never sent to the judge and never counted by a rule
    /// (ADR 0096).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PassedPage>? Passed { get; init; }
}

/// <summary>A hit a search listed and the lookup passed over, with why.</summary>
public sealed record PassedPage(string Url, string? Artist, string? Title, string Reason);

internal sealed record FoundPages(
    IReadOnlyList<FetchedPage> Pages,
    IReadOnlyList<FetchedPage> Candidates
)
{
    public IReadOnlyList<PassedPage> Passed { get; init; } = [];
}

internal sealed record HooktheoryCandidate(string Url, string Artist);

internal sealed record HooktheoryHits(
    IReadOnlyList<string> Confirmed,
    IReadOnlyList<HooktheoryCandidate> Candidates
)
{
    public static HooktheoryHits None { get; } = new([], []);

    /// <summary>What the search listed that is neither a page of this song nor a candidate.</summary>
    public IReadOnlyList<PassedPage> Passed { get; init; } = [];

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
        )
        {
            Passed =
            [
                .. all.SelectMany(hits => hits.Passed)
                    .Where(page =>
                        !confirmed.Contains(page.Url, StringComparer.OrdinalIgnoreCase)
                        && !all.Any(hits =>
                            hits.Candidates.Any(candidate =>
                                candidate.Url.Equals(page.Url, StringComparison.OrdinalIgnoreCase)
                            )
                        )
                    )
                    .DistinctBy(page => page.Url, StringComparer.OrdinalIgnoreCase),
            ],
        };
    }
}

public enum LookupStatus
{
    Found,
    NotFound,
    Unreachable,

    /// <summary>No lookup was made, for want of an artist and title; <c>Error</c> says why.</summary>
    NotLookedUp,
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

    /// <summary>Set where the person, not the lookup, put the page among those that count (ADR 0078).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageChoiceKind? Chosen { get; init; }
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
