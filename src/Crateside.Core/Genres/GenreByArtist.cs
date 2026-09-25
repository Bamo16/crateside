namespace Crateside.Core.Genres;

/// <summary>
/// What the user's own answers say about an artist: once enough of them name the same genre and
/// none names another, the artist's other tracks can take it without asking (#61).
/// </summary>
public static class GenreByArtist
{
    /// <summary>How many of the user's answers for one artist it takes. Guessed, not tuned.</summary>
    public const int AnswersNeeded = 3;

    /// <param name="answers">Each track's primary artist and the genre the user settled, null for none.</param>
    /// <returns>The genre each qualifying artist's tracks take, by artist, ignoring case.</returns>
    public static IReadOnlyDictionary<string, string> Learn(
        IEnumerable<(string Artist, string? Genre)> answers
    ) =>
        answers
            .GroupBy(answer => answer.Artist.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(artist =>
                artist.Count() >= AnswersNeeded
                // "None of these" is an answer too, and disagrees with every genre.
                && artist.Select(answer => answer.Genre).Distinct().ToArray() is [{ } _]
            )
            .ToDictionary(
                artist => artist.Key,
                artist => artist.First().Genre!,
                StringComparer.OrdinalIgnoreCase
            );
}
