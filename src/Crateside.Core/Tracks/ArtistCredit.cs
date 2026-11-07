using System.Text.RegularExpressions;

namespace Crateside.Core.Tracks;

/// <summary>A track's credited artists, split the way its name writes them.</summary>
public static partial class ArtistCredit
{
    [GeneratedRegex(@",|&|\+|／| x | feat\.? | ft\.? ", RegexOptions.IgnoreCase)]
    private static partial Regex Separator { get; }

    public static IReadOnlyList<string> Split(string credited) =>
        [.. Separator.Split(credited).Select(name => name.Trim()).Where(name => name.Length > 0)];

    /// <summary>The first credited artist, which is the one a track is filed under.</summary>
    public static string Primary(string credited) =>
        Split(credited) is [var first, ..] ? first : credited.Trim();
}
