using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a TheoryTab page says beyond each section's first key (#124).</summary>
public sealed record HooktheoryTab
{
    /// <summary>The analysts, without the site's own account and deleted users.</summary>
    public IReadOnlyList<string> Contributors { get; init; } = [];

    public IReadOnlyList<TabSection> Sections { get; init; } = [];

    /// <summary>The keys of the whole song as the site summarises them, such as <c>B dorian, B minor</c>.</summary>
    public string? AllKeys { get; init; }

    /// <summary>The YouTube video the sections are synced to, often not the recording being judged.</summary>
    public string? SyncedVideoId { get; init; }
}

/// <summary>One section of a TheoryTab; the percentages say how much of it is filled in.</summary>
public sealed record TabSection(
    string Name,
    string? Progression,
    double? PercentChord,
    double? PercentMelody
);

internal static partial class HooktheoryPageReader
{
    private static readonly string[] SiteAccounts = ["Hooktheory", "[deleted user]"];

    public static HooktheoryTab? Read(string page) =>
        SongObject(Flight(page)) is { } json && Parse(json) is { Sections.Count: > 0 } song
            ? new HooktheoryTab
            {
                Contributors =
                [
                    .. (song.Contributors?.Names ?? []).Where(name =>
                        !SiteAccounts.Contains(name, StringComparer.OrdinalIgnoreCase)
                    ),
                ],
                Sections =
                [
                    .. song.Sections.Select(section => new TabSection(
                        section.Name!,
                        section.Rel,
                        section.PctChord,
                        section.PctMelody
                    )),
                ],
                AllKeys =
                    song.SectionStats is { } stats && stats.TryGetValue("all", out var all)
                        ? string.Join(
                            ", ",
                            (all.AllKeys ?? []).Select(key => string.Join(' ', key))
                        )
                        : null,
                SyncedVideoId = song.YouTubeId,
            }
            : null;

    /// <summary>The song's sections that carry a name and a hash, once each; the page also lists a bare stub.</summary>
    internal static IReadOnlyList<SectionDto> Sections(string page) =>
        Parse(SongObject(Flight(page)))?.Sections ?? [];

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

    internal sealed record ContributorsDto(IReadOnlyList<string>? Names);

    internal sealed record SectionStatsDto(IReadOnlyList<IReadOnlyList<string>>? AllKeys);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HooktheoryPageReader.SongDto))]
internal sealed partial class HooktheoryJsonContext : JsonSerializerContext { }
