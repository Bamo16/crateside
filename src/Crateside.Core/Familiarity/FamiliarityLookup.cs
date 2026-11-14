using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Crateside.Core.Tracks;

namespace Crateside.Core.Familiarity;

/// <summary>
/// Asks ListenBrainz how many people played the song's recordings, and Wikimedia how many read its
/// own Wikipedia article, found through Wikidata and never by searching Wikipedia (ADR 0089). Needs
/// no key.
/// </summary>
public sealed class FamiliarityLookup
{
    public const string ClientName = "familiarity-lookup";

    /// <summary>Raised whenever matching changes, so songs an older lookup missed are asked again.</summary>
    public const int Version = 4;

    private const string Performer = "P175";

    private const string InstanceOf = "P31";

    private static readonly TimeSpan DefaultPace = TimeSpan.FromMilliseconds(600);

    /// <summary>Song, single, musical work and musical composition: the items a recording's song can be.</summary>
    private static readonly string[] SongTypes = ["Q7366", "Q134556", "Q105543609", "Q207628"];

    private readonly IHttpClientFactory _clients;
    private readonly TimeProvider _time;
    private readonly TimeSpan _pace;

    public FamiliarityLookup(IHttpClientFactory clients, TimeProvider time)
        : this(clients, time, DefaultPace) { }

    internal FamiliarityLookup(IHttpClientFactory clients, TimeProvider time, TimeSpan pace)
    {
        _clients = clients;
        _time = time;
        _pace = pace;
    }

    /// <summary>
    /// What the sources say, or null when one could not be reached — which is worth asking again,
    /// where a song they do not know is not.
    /// </summary>
    public async Task<FamiliarityReading?> ReadAsync(
        string artist,
        string title,
        CancellationToken ct = default
    )
    {
        try
        {
            var listeners = await ListenersAsync(artist, title, ct);
            var (item, article) = await ArticleAsync(artist, title, ct);
            var pageviews = article is null ? 0 : await PageviewsAsync(article, ct);

            return new FamiliarityReading(listeners, pageviews, item);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException
                && !ct.IsCancellationRequested
            )
        {
            return null;
        }
    }

    /// <summary>The most distinct users any one recording of the song has, or null when it has no recording.</summary>
    private async Task<int?> ListenersAsync(string artist, string title, CancellationToken ct)
    {
        if (await RecordingsAsync(artist, title, ct) is not [_, ..] recordings)
            return null;

        await Task.Delay(_pace, _time, ct);

        // ListenBrainz refuses a chunked body, so the length is known before the request goes.
        using var body = new StringContent(
            JsonSerializer.Serialize(
                new ListenBrainzQuery(recordings),
                FamiliarityJsonContext.Default.ListenBrainzQuery
            ),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _clients
            .CreateClient(ClientName)
            .PostAsync("https://api.listenbrainz.org/1/popularity/recording", body, ct);

        response.EnsureSuccessStatusCode();

        var popularity = await response.Content.ReadFromJsonAsync(
            FamiliarityJsonContext.Default.ListListenBrainzPopularity,
            ct
        );

        return popularity?.Max(one => one.TotalUserCount);
    }

    /// <summary>
    /// Every MusicBrainz recording of this title by one of the artists, since a song's listens are
    /// spread over its releases and the first hit is as likely as not an unplayed one.
    /// </summary>
    private async Task<IReadOnlyList<string>> RecordingsAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        var wanted = NameMatch.WithoutBrackets(title).Replace("\"", string.Empty);

        if (wanted.Length is 0)
            return [];

        var primary = ArtistCredit.Primary(artist).Replace("\"", string.Empty);

        await Task.Delay(_pace * 2, _time, ct);

        using var response = await _clients
            .CreateClient(ClientName)
            .GetAsync(
                $"https://musicbrainz.org/ws/2/recording?fmt=json&limit=100&query={Uri.EscapeDataString($"recording:\"{wanted}\" AND artist:\"{primary}\"")}",
                ct
            );

        response.EnsureSuccessStatusCode();

        using var search = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );

        return search.RootElement.TryGetProperty("recordings", out var recordings)
            ?
            [
                .. recordings
                    .EnumerateArray()
                    .Where(hit =>
                        hit.TryGetProperty("title", out var found)
                        && NameMatch.SameTitle(found.GetString() ?? string.Empty, wanted)
                        && CreditedNames(hit).Any(name => NameMatch.ByOneOf(artist, name))
                    )
                    .Select(hit => hit.GetProperty("id").GetString())
                    .OfType<string>()
                    .Distinct(),
            ]
            : [];
    }

    private static IEnumerable<string> CreditedNames(JsonElement recording) =>
        recording.TryGetProperty("artist-credit", out var credits)
            ? credits
                .EnumerateArray()
                .Select(credit =>
                    credit.TryGetProperty("name", out var name) ? name.GetString() : null
                )
                .OfType<string>()
            : [];

    /// <summary>
    /// The Wikidata item of a song or single whose performer is one of the artists and whose title
    /// is the song's, and the English Wikipedia article it links to, each null when there is none.
    /// </summary>
    private async Task<(string? Item, string? Article)> ArticleAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        var wanted = NameMatch.WithoutBrackets(title).Replace("\"", string.Empty);

        if (wanted.Length is 0)
            return (null, null);

        using var search = await WikidataAsync(
            $"action=query&list=search&srnamespace=0&srlimit=50&srsearch={Uri.EscapeDataString($"\"{wanted}\" haswbstatement:{Performer}")}",
            ct
        );

        string[] found =
        [
            .. search
                .RootElement.GetProperty("query")
                .GetProperty("search")
                .EnumerateArray()
                .Select(hit => hit.GetProperty("title").GetString())
                .OfType<string>(),
        ];

        if (found.Length is 0)
            return (null, null);

        using var items = await WikidataAsync(
            $"action=wbgetentities&props=claims|sitelinks|labels|aliases&languages=en&sitefilter=enwiki&ids={string.Join('|', found)}",
            ct
        );

        var entities = items.RootElement.GetProperty("entities");
        var candidates = found
            .Where(id => entities.TryGetProperty(id, out _))
            .Select(id => (Id: id, Entity: entities.GetProperty(id)))
            .Where(item =>
                Names(item.Entity).Any(name => NameMatch.SameTitle(name, wanted))
                && Related(item.Entity, InstanceOf).Intersect(SongTypes).Any()
            )
            .Select(item => (item.Id, item.Entity, Performers: Related(item.Entity, Performer)))
            .Where(item => item.Performers.Count > 0)
            .ToArray();

        if (candidates.Length is 0)
            return (null, null);

        string[] performerIds =
        [
            .. candidates.SelectMany(item => item.Performers).Distinct().Take(50),
        ];

        using var performers = await WikidataAsync(
            $"action=wbgetentities&props=labels|aliases&languages=en&ids={string.Join('|', performerIds)}",
            ct
        );

        var people = performers.RootElement.GetProperty("entities");
        var matching = candidates
            .Where(item =>
                item.Performers.Any(id =>
                    people.TryGetProperty(id, out var person)
                    && Names(person).Any(name => NameMatch.ByOneOf(artist, name))
                )
            )
            .Select(item => (item.Id, Article: ArticleOf(item.Entity)))
            .ToArray();

        return matching
            .Where(item => item.Article is not null)
            .Concat(matching)
            .Select(item => ((string?)item.Id, item.Article))
            .FirstOrDefault();
    }

    /// <summary>The views by people, not crawlers, over the last twelve whole months; none for an article with no views.</summary>
    private async Task<int> PageviewsAsync(string article, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var name = Uri.EscapeDataString(article.Replace(' ', '_'));
        var window = $"{Day(thisMonth.AddMonths(-12))}/{Day(thisMonth.AddDays(-1))}";

        await Task.Delay(_pace, _time, ct);

        using var response = await _clients
            .CreateClient(ClientName)
            .GetAsync(
                $"https://wikimedia.org/api/rest_v1/metrics/pageviews/per-article/en.wikipedia/all-access/user/{name}/monthly/{window}",
                ct
            );

        if (response.StatusCode is HttpStatusCode.NotFound)
            return 0;

        response.EnsureSuccessStatusCode();

        using var views = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );

        return views
            .RootElement.GetProperty("items")
            .EnumerateArray()
            .Sum(month => month.GetProperty("views").GetInt32());
    }

    private async Task<JsonDocument> WikidataAsync(string query, CancellationToken ct)
    {
        await Task.Delay(_pace, _time, ct);

        using var response = await _clients
            .CreateClient(ClientName)
            .GetAsync($"https://www.wikidata.org/w/api.php?format=json&{query}", ct);

        response.EnsureSuccessStatusCode();

        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );

        if (!document.RootElement.TryGetProperty("error", out _))
            return document;

        document.Dispose();

        throw new HttpRequestException("Wikidata refused the request.");
    }

    private static string Day(DateTime day) =>
        day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string? ArticleOf(JsonElement entity) =>
        entity.TryGetProperty("sitelinks", out var links)
        && links.TryGetProperty("enwiki", out var wiki)
            ? wiki.GetProperty("title").GetString()
            : null;

    /// <summary>The label and the English aliases of a Wikidata entity.</summary>
    private static IEnumerable<string> Names(JsonElement entity)
    {
        if (
            entity.TryGetProperty("labels", out var labels)
            && labels.TryGetProperty("en", out var label)
            && label.GetProperty("value").GetString() is { } name
        )
            yield return name;

        if (
            entity.TryGetProperty("aliases", out var aliases)
            && aliases.TryGetProperty("en", out var english)
        )
        {
            foreach (var alias in english.EnumerateArray())
            {
                if (alias.GetProperty("value").GetString() is { } other)
                    yield return other;
            }
        }
    }

    /// <summary>The items an entity's statements of one property point at.</summary>
    private static IReadOnlyList<string> Related(JsonElement entity, string property) =>
        entity.TryGetProperty("claims", out var claims)
        && claims.TryGetProperty(property, out var statements)
            ?
            [
                .. statements
                    .EnumerateArray()
                    .Select(statement =>
                        statement.TryGetProperty("mainsnak", out var snak)
                        && snak.TryGetProperty("datavalue", out var data)
                        && data.TryGetProperty("value", out var value)
                        && value.TryGetProperty("id", out var id)
                            ? id.GetString()
                            : null
                    )
                    .OfType<string>(),
            ]
            : [];
}
