using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ledger;

/// <summary>
/// A song the ledger already knows under a name that may differ only in how its credits are
/// written: the same bare title, and at least one artist in common (#41).
/// </summary>
public static partial class Lookalikes
{
    /// <summary>The song <paramref name="name"/> looks like, most recently judged first.</summary>
    public static bool TryFind(
        string? name,
        KeyLedger ledger,
        [NotNullWhen(true)] out LedgerRun? song
    )
    {
        song = TryKey(name, out var wanted)
            ? ledger
                .Latest.Where(run => TryKey(run.Song, out var known) && wanted.IsLike(known))
                .MaxBy(run => run.Timestamp)
            : null;

        return song is not null;
    }

    private static bool TryKey(string? name, [NotNullWhen(true)] out SongKey? key)
    {
        key = null;

        if (name?.Split(" - ", 2) is not [var artists, var title])
            return false;

        List<string> credited = [artists];

        title = Noise.Replace(title, string.Empty);

        foreach (Match feature in BracketedFeature.Matches(title))
            credited.Add(feature.Groups[1].Value);

        title = BracketedFeature.Replace(title, string.Empty);

        if (TrailingFeature.Match(title) is { Success: true } inTitle)
        {
            credited.Add(inTitle.Groups[1].Value);
            title = title[..inTitle.Index];
        }

        if (TrailingFeature.Match(artists) is { Success: true } inArtists)
            credited[0] = artists[..inArtists.Index];

        var names = credited
            .SelectMany(part => ArtistSeparator.Split(part))
            .Select(Fold)
            .Where(artist => artist.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (Fold(title) is not { Length: > 0 } bare || names.Count is 0)
            return false;

        key = new SongKey(names, bare);

        return true;
    }

    /// <summary>Lowercase ASCII letters, digits and spaces, with accents folded away.</summary>
    private static string Fold(string text)
    {
        var letters = text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) is not UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or ' ')
            .ToArray();

        return string.Join(
            ' ',
            new string(letters).Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    [GeneratedRegex(
        @"\s*[\(\[]\s*(?:feat\.?|ft\.?|featuring|with)\s+([^\)\]]*)[\)\]]",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex BracketedFeature { get; }

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s+(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingFeature { get; }

    [GeneratedRegex(@"\s*(?:,|&|\+|\band\b|\bx\b|\bvs\.?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparator { get; }

    [GeneratedRegex(
        @"\s*[\(\[][^\)\]]*\b(?:official|video|audio|lyrics?|remaster(?:ed)?|visuali[sz]er|hd|4k)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex Noise { get; }

    private sealed record SongKey(IReadOnlySet<string> Artists, string Title)
    {
        public bool IsLike(SongKey other) =>
            Title.Equals(other.Title, StringComparison.Ordinal) && Artists.Overlaps(other.Artists);
    }
}
