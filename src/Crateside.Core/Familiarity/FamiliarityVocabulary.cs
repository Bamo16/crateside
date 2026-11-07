namespace Crateside.Core.Familiarity;

/// <summary>
/// How likely a listener is to know a song, as a tag group of crateside's own: the axis mashup
/// pairs are chosen on, one familiar half carrying one new one (#52).
/// </summary>
public static class FamiliarityVocabulary
{
    public const string Group = "Familiarity";

    public const string Mainstream = "Mainstream";
    public const string Obscure = "Obscure";

    public static IReadOnlyList<string> All { get; } = [Mainstream, Obscure];

    public static string Keyword(string value) => $"{Group}|{value}";

    public static bool IsValue(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    public static bool IsKeyword(string keyword) =>
        keyword.StartsWith($"{Group}|", StringComparison.Ordinal);

    /// <summary>
    /// The value the numbers settle on their own, or null for the middle band the user decides:
    /// the artist first, then the song, which catches a one-hit wonder by a small artist.
    /// </summary>
    public static string? FromReach(long? artistFans, int? songRank) =>
        (artistFans, songRank) switch
        {
            (>= 150_000, _) => Mainstream,
            (_, >= 600_000) => Mainstream,
            (< 20_000, null or < 400_000) => Obscure,
            _ => null,
        };
}
