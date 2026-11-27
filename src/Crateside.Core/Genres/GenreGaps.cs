namespace Crateside.Core.Genres;

/// <summary>
/// What the sources called the songs the table does not map, most common first — the signal that
/// the table wants another line (ADR 0088).
/// </summary>
public static class GenreGaps
{
    /// <summary>Songs counted under each label that maps onto nothing; a song counts once per label.</summary>
    public static IReadOnlyList<GenreGap> From(IEnumerable<GenreRecord> records) =>
        [
            .. records
                .SelectMany(record =>
                    record
                        .Heard.Where(heard => GenreMapping.Map(heard).Count is 0)
                        .Select(GenreMapping.Normalise)
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
