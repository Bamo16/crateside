using System.Globalization;
using System.Text;
using Raffinert.FuzzySharp;

namespace Crateside.Core.Tracks;

/// <summary>What a search found: the rows, and whether they are near matches rather than exact ones.</summary>
public sealed record TrackSearchResult(IReadOnlyList<TrackRow> Rows, bool NearMatches);

/// <summary>
/// The names a row answers to when searched, folded once when the row is built so a keystroke
/// folds only the query (#230).
/// </summary>
internal sealed record SearchNames(
    string[] Whole,
    string[] Squashed,
    string[] Fuzzy,
    string[] UploadIds
);

/// <summary>How the Tracks search finds a row (#230).</summary>
public static class TrackSearch
{
    /// <summary>
    /// The lowest <c>WeightedRatio</c> a row's name may score and still be a near match: measured on
    /// the real ledger, the worst misspelling scored 80 and the best nonsense 68.
    /// </summary>
    internal const int NearCutoff = 75;

    private const int NearLimit = 20;

    private static readonly Dictionary<char, string> Unfolded = new()
    {
        ['ø'] = "o",
        ['ß'] = "ss",
        ['æ'] = "ae",
        ['ł'] = "l",
        ['đ'] = "d",
    };

    /// <summary>
    /// The one form a query and every searched name are compared in: lower case, Latin accents
    /// dropped, apostrophes deleted, every other punctuation mark a space, runs of spaces one.
    /// </summary>
    public static string Fold(string? text)
    {
        if (text is not { Length: > 0 })
            return string.Empty;

        var folded = new StringBuilder(text.Length);

        foreach (var letter in text.ToLowerInvariant())
        {
            if (letter is '\'' or '’')
                continue;

            if (Unfolded.TryGetValue(letter, out var replacement))
            {
                folded.Append(replacement);
            }
            else if (letter >= '\u0080' && IsLatin(letter))
            {
                foreach (var part in letter.ToString().Normalize(NormalizationForm.FormKD))
                {
                    if (
                        CharUnicodeInfo.GetUnicodeCategory(part)
                        is not UnicodeCategory.NonSpacingMark
                    )
                        AppendLetter(folded, part);
                }
            }
            else
            {
                AppendLetter(folded, letter);
            }
        }

        return string.Join(
            ' ',
            folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    /// <summary>
    /// Whether the row answers the search exactly: each word of it is inside one of the row's
    /// names (spaces removed), or it is the video ID of the row or one of its uploads.
    /// </summary>
    public static bool Matches(this TrackRow row, string? search) =>
        search?.Trim() is not { Length: > 0 } term
        || IsExact(NamesOf(row), Words(Fold(term)), term);

    /// <summary>
    /// The rows answering the search, each tier in the order the rows come: the video ID, a name
    /// holding the whole search as words in order, then the rest. When no row matches exactly, the
    /// closest in spelling instead.
    /// </summary>
    public static TrackSearchResult Search(this IEnumerable<TrackRow> rows, string search)
    {
        var folded = Fold(search);
        var words = Words(folded);
        var term = search.Trim();

        if (words.Length is 0)
            return new([], false);

        IReadOnlyList<TrackRow> everyRow = rows as IReadOnlyList<TrackRow> ?? [.. rows];
        var exact = everyRow
            .Select((row, index) => (Row: row, Names: NamesOf(row), Index: index))
            .Where(entry => IsExact(entry.Names, words, term))
            .ToArray();

        if (exact.Length is 0)
            return new(NearMatches(everyRow, folded), true);

        var id = YouTubeUrl.TryGetVideoId(term, out var videoId) ? videoId : null;
        var phrase = $" {folded} ";

        int Tier(SearchNames names) =>
            id is not null && names.UploadIds.Contains(id, StringComparer.Ordinal) ? 0
            : names.Whole.Any(name => $" {name} ".Contains(phrase, StringComparison.Ordinal)) ? 1
            : 2;

        return new(
            [
                .. exact
                    .OrderBy(entry => Tier(entry.Names))
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Row),
            ],
            false
        );
    }

    /// <summary>What <paramref name="row"/> is searched by: the names it was built with, else read now.</summary>
    internal static SearchNames NamesOf(TrackRow row) => row.SearchNames ?? Build(row);

    /// <summary>Reads the row's names, upload titles included.</summary>
    internal static SearchNames Build(TrackRow row)
    {
        string?[] own =
        [
            row.Title,
            row.DerivedTitle,
            row.Verdict?.Artist,
            row.Verdict?.Title,
            row.TagArtist,
            row.TagTitle,
        ];
        string[] uploadIds =
        [
            .. row.Facts.Uploads.Prepend(row.TrackKey).Distinct(StringComparer.Ordinal),
        ];

        var fuzzy = own.Select(Fold).Where(name => name.Length > 0).Distinct().ToArray();
        string[] whole =
        [
            .. fuzzy,
            .. uploadIds
                .Where(_ => row.HasUpload)
                .Select(upload => Fold(UploadTitles.Of(row, upload)))
                .Where(name => name.Length > 0),
        ];

        return new(
            whole,
            [.. whole.Select(name => name.Replace(" ", string.Empty, StringComparison.Ordinal))],
            fuzzy,
            uploadIds
        );
    }

    /// <summary>How close the row's best name is to the folded search, 0 to 100; upload titles are left out.</summary>
    internal static double Score(TrackRow row, string folded) =>
        NamesOf(row)
            .Fuzzy.Select(name => Fuzz.WeightedRatio(folded, name, static text => text))
            .DefaultIfEmpty(0)
            .Max();

    private static string[] Words(string folded) =>
        folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsExact(SearchNames names, string[] words, string term) =>
        (
            YouTubeUrl.TryGetVideoId(term, out var id)
            && names.UploadIds.Contains(id, StringComparer.Ordinal)
        )
        || words.All(word =>
            names.Squashed.Any(name => name.Contains(word, StringComparison.Ordinal))
        );

    private static TrackRow[] NearMatches(IReadOnlyList<TrackRow> rows, string folded) =>
        [
            .. rows.Select((row, index) => (Row: row, Score: Score(row, folded), Index: index))
                .Where(entry => entry.Score >= NearCutoff)
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Index)
                .Take(NearLimit)
                .Select(entry => entry.Row),
        ];

    private static bool IsLatin(char letter) => letter is < 'ɐ' or (>= 'Ḁ' and <= 'ỿ');

    private static void AppendLetter(StringBuilder into, char letter)
    {
        into.Append(
            char.IsPunctuation(letter) || char.IsSymbol(letter) || char.IsWhiteSpace(letter)
                ? ' '
                : letter
        );
    }
}
