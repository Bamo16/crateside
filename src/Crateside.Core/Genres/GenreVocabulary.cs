namespace Crateside.Core.Genres;

/// <summary>
/// The closed list of genres, in Ableton's spelling, under a tag group of crateside's own —
/// extended only by a code change (ADR 0030).
/// </summary>
public static class GenreVocabulary
{
    public const string Group = "Genre";

    public static IReadOnlyList<string> All { get; } =
    ["Drum & Bass", "Future Bass", "Hip Hop", "J-Pop", "K-Pop", "Pop", "Rock"];

    public static string Keyword(string genre) => $"{Group}|{genre}";

    public static bool IsGenre(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    public static bool IsGenreKeyword(string keyword) =>
        keyword.StartsWith($"{Group}|", StringComparison.Ordinal);

    /// <summary>
    /// The genre a source's own label maps onto, or null when it maps onto none of the list. The
    /// more specific genres are tried first, so <c>k-pop</c> is not Pop and <c>pop rock</c> is Rock.
    /// </summary>
    public static string? FromHeard(string? heard) =>
        Words(heard) switch
        {
            [] => null,
            var w
                when w.Has("drum", "and", "bass")
                    || w.Has("drum", "n", "bass")
                    || w.Has("drum", "bass")
                    || w.Has("dnb") => "Drum & Bass",
            var w when w.Has("future", "bass") => "Future Bass",
            var w when w.Has("k", "pop") || w.Has("kpop") => "K-Pop",
            var w when w.Has("j", "pop") || w.Has("jpop") => "J-Pop",
            var w when w.Has("hip", "hop") || w.Has("hiphop") || w.Has("rap") => "Hip Hop",
            var w when w.Has("rock") => "Rock",
            var w when w.Has("pop") || w.Has("synthpop") || w.Has("electropop") => "Pop",
            _ => null,
        };

    /// <summary>Lowercase words, split on anything that is not a letter or digit.</summary>
    private static string[] Words(string? text) =>
        text is null
            ? []
            : text.ToLowerInvariant()
                .Split(
                    [.. text.Where(c => !char.IsLetterOrDigit(c)).Distinct()],
                    StringSplitOptions.RemoveEmptyEntries
                );

    /// <summary>Whether the words run in this order somewhere in the label.</summary>
    private static bool Has(this string[] words, params string[] run) =>
        words.Length >= run.Length
        && Enumerable
            .Range(0, words.Length - run.Length + 1)
            .Any(start => words.AsSpan(start, run.Length).SequenceEqual(run));
}
