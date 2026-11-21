namespace Crateside.Core.Genres;

/// <summary>
/// Ableton's built-in <c>Genres</c> tags, as Ableton ships them (ADR 0088). The person's own
/// genres share the group but are not on this list.
/// </summary>
public static class GenreVocabulary
{
    public const string Group = "Genres";

    /// <summary>The group crateside wrote genres under before ADR 0088; its keywords are taken off.</summary>
    public const string RetiredGroup = "Genre";

    public static IReadOnlyList<string> All { get; } =
    [
        "Ambient",
        "Blues",
        "Breakbeat",
        "Dancehall",
        "Disco",
        "Drum & Bass",
        "Dub",
        "Dubstep",
        "EDM",
        "Electro",
        "Electronica",
        "Experimental",
        "Folk",
        "Funk",
        "Garage",
        "Hip Hop",
        "House",
        "Indie",
        "Jazz",
        "Latin",
        "Pop",
        "R&B",
        "Reggae",
        "Reggaeton",
        "Rock",
        "Soul",
        "Synthpop",
        "Techno",
        "Trap",
    ];

    public static string Keyword(string genre) => $"{Group}|{genre}";

    public static bool IsGenre(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>Whether this can be a genre of the person's own: a name that is a whole keyword value.</summary>
    public static bool IsName(string? value) =>
        value is { Length: > 0 }
        && value == value.Trim()
        && value.All(c => c is not ('|' or < ' '));

    public static bool IsGenreKeyword(string keyword) =>
        keyword.StartsWith($"{Group}|", StringComparison.Ordinal);

    public static bool IsRetiredKeyword(string keyword) =>
        keyword.StartsWith($"{RetiredGroup}|", StringComparison.Ordinal);
}
