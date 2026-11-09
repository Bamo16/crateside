using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crateside.Core.Tracks;

namespace Crateside.Core;

/// <summary>Comparing a song's title or artist as one source spells it with another's.</summary>
internal static partial class NameMatch
{
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Bracketed { get; }

    [GeneratedRegex(@"[^\p{L}\p{N}*]+")]
    private static partial Regex NotLetterOrDigit { get; }

    public static string WithoutBrackets(string text) =>
        Bracketed.Replace(text, string.Empty).Trim();

    /// <summary>Lowercase letters, digits and <c>*</c> only, accents folded and <c>$</c> read as S.</summary>
    public static string Squash(string text)
    {
        var folded = Bracketed
            .Replace(text, string.Empty)
            .Replace('$', 's')
            .Replace("&", "and")
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) is not UnicodeCategory.NonSpacingMark)
            .ToArray();

        return NotLetterOrDigit.Replace(new string(folded), string.Empty).ToLowerInvariant();
    }

    /// <summary>The same title once both are squashed to letters and digits.</summary>
    public static bool SameTitle(string found, string wanted) =>
        Squash(found) is { Length: > 0 } squashed && squashed == Squash(wanted);

    /// <summary>
    /// Whether a source's artist is the credited one, or one of several credited, however either
    /// spells the name: the whole name, not a part of a longer one.
    /// </summary>
    public static bool ByOneOf(string credited, string found) =>
        Plain(found) is { Length: > 0 } by
        && ArtistCredit.Split(credited).Append(credited).Any(one => Plain(one) == by);

    private static string Plain(string name) => Squash(name).Replace("*", string.Empty);
}
