using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a TheoryTab says beyond each section's first key (#124).</summary>
public sealed record HooktheoryTab
{
    /// <summary>The analysts, without the site's own account and deleted users.</summary>
    public IReadOnlyList<string> Contributors { get; init; } = [];

    public IReadOnlyList<TabSection> Sections { get; init; } = [];

    /// <summary>The keys of the whole song as the site summarizes them, such as <c>B dorian, B minor</c>.</summary>
    public string? AllKeys { get; init; }

    /// <summary>The YouTube video the sections are synced to, often not the recording being judged.</summary>
    [JsonPropertyName("syncedVideoId")]
    public string? SyncedTrackKey { get; init; }

    /// <summary>The share of the synced video, 0 to 1, that the sections synced to it span together; null when none is.</summary>
    public double? Coverage { get; init; }

    /// <summary>One contributor and one or two sections; Rule 1 of the judge's prompt does not cap on such a page (ADR 0062).</summary>
    public bool IsThin => Contributors is [_] && Sections.Count is 1 or 2;
}

/// <summary>One section of a TheoryTab; the percentages say how much of it is filled in.</summary>
public sealed record TabSection(
    string Name,
    string? Progression,
    double? PercentChord,
    double? PercentMelody
)
{
    /// <summary>Every key the section is in, with the beat it starts at; more than one means it modulates inside the section.</summary>
    public IReadOnlyList<TabKey> Keys { get; init; } = [];

    public int? Chords { get; init; }

    /// <summary>The chords as its score names them, by the key in force at each beat; the page's numerals stay in the section's first key.</summary>
    public IReadOnlyList<TabChord> Harmony { get; init; } = [];

    /// <summary>The video the section is synced to, when it is not the song's.</summary>
    [JsonPropertyName("videoId")]
    public string? TrackKey { get; init; }

    /// <summary>Where the section starts and ends in its video, as fractions of the video's length.</summary>
    public double? SyncStart { get; init; }

    public double? SyncEnd { get; init; }
}

public sealed record TabKey(double Beat, string Key);

/// <summary>A chord of a section's score, as a Harte label such as <c>C:maj7/3</c>.</summary>
public sealed record TabChord(double Beat, string Label);

internal static partial class HooktheoryPageReader
{
    private static readonly string[] SiteAccounts = ["Hooktheory", "[deleted user]"];

    /// <summary>The song object in the page's payload, or null for a page that has none.</summary>
    public static SongDto? Read(string page) => Parse(SongObject(Flight(page)));

    /// <summary>The tab, with each section's own score where its hash is in <paramref name="scores"/>.</summary>
    public static HooktheoryTab? Tab(SongDto? song, IReadOnlyDictionary<string, string>? scores)
    {
        if (song is not { Sections.Count: > 0 })
            return null;

        var sections = song
            .Sections.Select(section =>
                new TabSection(section.Name!, section.Rel, section.PctChord, section.PctMelody)
                    is var tab
                && scores?.GetValueOrDefault(section.Hash!) is { } body
                && ReadScore(body) is { } score
                    ? tab with
                    {
                        Keys =
                        [
                            .. (score.Keys ?? []).Select(key => new TabKey(
                                key.Beat,
                                $"{key.Tonic} {key.Scale}"
                            )),
                        ],
                        Chords = score.Chords?.Count(chord => chord.IsRest is not true),
                        Harmony = HookpadChord.Read(score),
                        TrackKey = score.Youtube?.Id is { } id && id != song.YouTubeId ? id : null,
                        SyncStart =
                            score.Youtube is { SyncStart: { } start, SyncEnd: { } end }
                            && end > start
                                ? start
                                : null,
                        SyncEnd =
                            score.Youtube is { SyncStart: { } from, SyncEnd: { } to } && to > from
                                ? to
                                : null,
                    }
                    : tab
            )
            .ToList();

        return new HooktheoryTab
        {
            Contributors =
            [
                .. (song.Contributors?.Names ?? []).Where(name =>
                    !SiteAccounts.Contains(name, StringComparer.OrdinalIgnoreCase)
                ),
            ],
            Sections = sections,
            AllKeys =
                song.SectionStats is { } stats && stats.TryGetValue("all", out var all)
                    ? string.Join(", ", (all.AllKeys ?? []).Select(key => string.Join(' ', key)))
                    : null,
            SyncedTrackKey = song.YouTubeId,
            Coverage = Coverage(sections),
        };
    }

    /// <summary>The sections synced to the song's own video, merged where they overlap.</summary>
    private static double? Coverage(IReadOnlyList<TabSection> sections)
    {
        var spans = sections
            .Where(section => section is { TrackKey: null, SyncStart: not null, SyncEnd: not null })
            .OrderBy(section => section.SyncStart)
            .ToList();

        if (spans is [])
            return null;

        var covered = 0.0;
        var reached = 0.0;

        foreach (var span in spans)
        {
            var (start, end) = (Math.Max(span.SyncStart!.Value, reached), span.SyncEnd!.Value);

            if (end > start)
            {
                covered += end - start;
                reached = end;
            }
        }

        return covered;
    }

    private static ScoreDto? ReadScore(string body)
    {
        try
        {
            return
                JsonSerializer
                    .Deserialize(body, HooktheoryJsonContext.Default.ApiResponseDto)
                    ?.JsonData
                    is { Length: > 0 } json
                ? JsonSerializer.Deserialize(json, HooktheoryJsonContext.Default.ScoreDto)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SongDto? Parse(string? json)
    {
        if (json is null)
            return null;

        try
        {
            return
                JsonSerializer.Deserialize(json, HooktheoryJsonContext.Default.SongDto) is { } song
                ? song with
                {
                    Sections =
                    [
                        .. song
                            .Sections.Where(section =>
                                section is { Name.Length: > 0, Hash.Length: > 0 }
                            )
                            .DistinctBy(section => section.Name),
                    ],
                }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The page's Next.js payload: the strings of every <c>self.__next_f.push</c>, joined.</summary>
    private static string Flight(string page)
    {
        var flight = new StringBuilder();

        foreach (var push in FlightPush.Matches(page).Select(match => match.Groups["Array"].Value))
        {
            try
            {
                if (
                    JsonSerializer.Deserialize<JsonElement>(push)
                        is { ValueKind: JsonValueKind.Array } array
                    && array.GetArrayLength() > 1
                    && array[1] is { ValueKind: JsonValueKind.String } chunk
                )
                    flight.Append(chunk.GetString());
            }
            catch (JsonException)
            {
                // a push that is not JSON is not the payload
            }
        }

        return flight.ToString();
    }

    /// <summary>The JSON object that encloses the song's section list.</summary>
    private static string? SongObject(string flight)
    {
        if (flight.IndexOf(SectionsAnchor, StringComparison.Ordinal) is var anchor && anchor < 0)
            return null;

        var start = anchor;

        for (var depth = 0; start > 0; )
        {
            start--;

            if (flight[start] == '}')
                depth++;
            else if (flight[start] == '{')
            {
                if (depth == 0)
                    break;

                depth--;
            }
        }

        var (inString, escaped, level) = (false, false, 0);

        for (var end = start; end < flight.Length; end++)
        {
            var c = flight[end];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
                inString = true;
            else if (c == '{')
                level++;
            else if (c == '}' && --level == 0)
                return flight[start..(end + 1)];
        }

        return null;
    }

    private const string SectionsAnchor = "\"sections\":[{\"wikiHash\"";

    [GeneratedRegex(@"self\.__next_f\.push\((?<Array>\[.*?\])\)</script>", RegexOptions.Singleline)]
    private static partial Regex FlightPush { get; }

    internal sealed record SongDto
    {
        public IReadOnlyList<SectionDto> Sections { get; init; } = [];
        public ContributorsDto? Contributors { get; init; }
        public Dictionary<string, SectionStatsDto>? SectionStats { get; init; }

        [JsonPropertyName("youTubeID")]
        public string? YouTubeId { get; init; }
    }

    internal sealed record SectionDto
    {
        public string? Name { get; init; }
        public string? Hash { get; init; }
        public string? Key { get; init; }
        public string? Scale { get; init; }
        public double? Tempo { get; init; }
        public string? Rel { get; init; }
        public double? PctChord { get; init; }
        public double? PctMelody { get; init; }
    }

    internal sealed record ApiResponseDto(string? JsonData);

    internal sealed record ScoreDto
    {
        public IReadOnlyList<ScoreKeyDto>? Keys { get; init; }
        public IReadOnlyList<ScoreChordDto>? Chords { get; init; }
        public YoutubeDto? Youtube { get; init; }
    }

    internal sealed record ScoreKeyDto(double Beat, string Tonic, string Scale);

    internal sealed record ScoreChordDto
    {
        public double Beat { get; init; }
        public int Root { get; init; }
        public int Type { get; init; }
        public int? Inversion { get; init; }

        /// <summary>The degree of the target's major scale; the target is <see cref="Root"/>.</summary>
        public int? Applied { get; init; }

        /// <summary>A mode name or seven semitone offsets that replace the key's mode; empty or null for none.</summary>
        public JsonElement Borrowed { get; init; }

        public IReadOnlyList<int>? Adds { get; init; }
        public IReadOnlyList<int>? Omits { get; init; }
        public IReadOnlyList<int>? Suspensions { get; init; }
        public IReadOnlyList<string>? Alterations { get; init; }
        public IReadOnlyList<string>? Substitutions { get; init; }
        public bool? IsRest { get; init; }
    }

    internal sealed record YoutubeDto(string? Id, double? SyncStart, double? SyncEnd);

    internal sealed record ContributorsDto(IReadOnlyList<string>? Names);

    internal sealed record SectionStatsDto(IReadOnlyList<IReadOnlyList<string>>? AllKeys);
}

internal sealed record HooktheorySearchQuery(string Q)
{
    public int Limit => 50;

    public string[] AttributesToRetrieve => ["artist", "song", "artistSlug", "songSlug"];
}

internal sealed record HooktheorySearchResponse(IReadOnlyList<HooktheorySearchHit>? Hits);

internal sealed record HooktheorySearchHit(
    string? Artist,
    string? Song,
    string? ArtistSlug,
    string? SongSlug
);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HooktheoryPageReader.SongDto))]
[JsonSerializable(typeof(HooktheoryPageReader.ApiResponseDto))]
[JsonSerializable(typeof(HooktheoryPageReader.ScoreDto))]
[JsonSerializable(typeof(HooktheorySearchQuery))]
[JsonSerializable(typeof(HooktheorySearchResponse))]
internal sealed partial class HooktheoryJsonContext : JsonSerializerContext { }
