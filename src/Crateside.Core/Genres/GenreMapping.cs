using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crateside.Core.Genres;

/// <summary>
/// Maps a source's label onto one or two of Ableton's 29 genres, from the table shipped as
/// <c>genre-labels.json</c> (ADR 0088).
/// </summary>
public static partial class GenreMapping
{
    private const int MostPerSong = 2;

    private static readonly Dictionary<string, string[]> Table = Load();

    [GeneratedRegex(@"[^\p{L}\p{N}&]+")]
    private static partial Regex Separators { get; }

    /// <summary>Lowercase, apostrophes dropped, <c>&amp;</c> between words read as "and", every other run of punctuation a space.</summary>
    public static string Normalise(string label) =>
        Separators
            .Replace(
                label.ToLowerInvariant().Replace("'", string.Empty).Replace(" & ", " and "),
                " "
            )
            .Trim();

    /// <summary>What the label maps onto; empty when the table does not map it.</summary>
    public static IReadOnlyList<string> Map(string label) =>
        Table.GetValueOrDefault(Normalise(label)) ?? [];

    /// <summary>
    /// The genres most of a song's labels map to, best first and at most two: the first when the
    /// labels agree, the second only when at least half as many labels back it. Ties go to the
    /// label heard first.
    /// </summary>
    public static IReadOnlyList<string> Pick(IEnumerable<string> labels) =>
        labels
            .Select(Normalise)
            .Distinct()
            .SelectMany(label => Table.GetValueOrDefault(label) ?? [])
            .GroupBy(genre => genre)
            .Select(genre => (Genre: genre.Key, Labels: genre.Count()))
            .OrderByDescending(genre => genre.Labels)
            .ToArray() switch
        {
            [] => [],
            var ranked =>
            [
                .. ranked
                    .Take(MostPerSong)
                    .Where(genre => genre.Labels * 2 >= ranked[0].Labels)
                    .Select(genre => genre.Genre),
            ],
        };

    private static Dictionary<string, string[]> Load()
    {
        using var stream =
            typeof(GenreMapping).Assembly.GetManifestResourceStream("genre-labels.json")
            ?? throw new InvalidOperationException("genre-labels.json is not embedded.");

        return JsonSerializer.Deserialize(
                stream,
                GenreJsonContext.Default.DictionaryStringStringArray
            ) ?? [];
    }
}
