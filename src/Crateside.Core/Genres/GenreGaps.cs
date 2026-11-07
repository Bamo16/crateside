namespace Crateside.Core.Genres;

/// <summary>
/// What the sources called the songs the list does not cover, most common first — the signal that
/// the list wants another genre (ADR 0030).
/// </summary>
public static class GenreGaps
{
    /// <summary>
    /// Songs settled as none of the list, or suggested nothing, counted under each label that maps
    /// onto nothing; a song counts once per label.
    /// </summary>
    public static IReadOnlyList<GenreGap> From(IEnumerable<GenreRecord> records) =>
        [
            .. records
                .Where(record =>
                    record
                        is { IsSettled: true, Genre: null }
                            or { IsSettled: false, Suggested: null, LookedUpAt: not null }
                )
                .SelectMany(record =>
                    record
                        .Heard.Where(heard => GenreVocabulary.FromHeard(heard) is null)
                        .Select(heard => heard.Trim().ToLowerInvariant())
                        .Distinct()
                        .Select(label => (Label: label, record.Song))
                )
                .GroupBy(entry => entry.Label)
                .Select(label => new GenreGap(label.Key, label.Count()))
                .OrderByDescending(gap => gap.Songs)
                .ThenBy(gap => gap.Label, StringComparer.Ordinal),
        ];
}

public sealed record GenreGap(string Label, int Songs);
