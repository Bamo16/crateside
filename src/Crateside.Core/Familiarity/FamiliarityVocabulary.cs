namespace Crateside.Core.Familiarity;

/// <summary>
/// How likely a listener is to know a song, as a tag group of crateside's own: the axis mashup
/// pairs are chosen on, one familiar half carrying one new one (ADR 0089).
/// </summary>
public static class FamiliarityVocabulary
{
    public const string Group = "Familiarity";

    public const string Mainstream = "Mainstream";
    public const string Niche = "Niche";
    public const string Obscure = "Obscure";

    public const int ListenerCut = 3_500;
    public const int PageviewCut = 20_000;

    public static IReadOnlyList<string> All { get; } = [Mainstream, Niche, Obscure];

    public static string Keyword(string value) => $"{Group}|{value}";

    public static bool IsValue(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    public static bool IsKeyword(string keyword) =>
        keyword.StartsWith($"{Group}|", StringComparison.Ordinal);

    /// <summary>
    /// Two votes: the song's ListenBrainz listeners and its Wikipedia article's yearly pageviews.
    /// Both is mainstream, neither obscure, one niche; a missing number is a vote against.
    /// </summary>
    public static string FromReach(int? listeners, int? pageviews) =>
        (listeners >= ListenerCut, pageviews >= PageviewCut) switch
        {
            (true, true) => Mainstream,
            (false, false) => Obscure,
            _ => Niche,
        };
}
