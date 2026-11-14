using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Vocals;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>A descriptive keyword the person added to a file in Live, or took off one crateside wrote.</summary>
/// <param name="Song">The ledger's song identity of the file's track.</param>
/// <param name="Stem">The stem file's identity, for the answer that stays on the file.</param>
public sealed record LiveEdit(string Song, string Stem, string Keyword, bool Removed)
{
    /// <summary>Names this removal of this keyword from this stem file.</summary>
    public string Removal => $"{Stem}|{Keyword}";
}

/// <summary>
/// Records the descriptive tags the person added or removed in Live as their answers: genre and
/// familiarity for the song, Lead or Sparse for the stem file (ADR 0087).
/// </summary>
public sealed class LiveAnswers(
    GenreStore genres,
    FamiliarityStore familiarity,
    VocalAnswerStore vocals
)
{
    /// <summary>The keywords crateside has asked to be written onto a song's files and still stands behind.</summary>
    public IReadOnlySet<string> WrittenFor(string song)
    {
        var genre = genres.Read().GetValueOrDefault(song);
        var reach = familiarity.Read().GetValueOrDefault(song);

        return new HashSet<string>(
            [
                .. (genre?.Tagged.Except(genre.Rejected) ?? []).Select(GenreVocabulary.Keyword),
                .. (reach?.Tagged.Except(reach.Rejected) ?? []).Select(
                    FamiliarityVocabulary.Keyword
                ),
            ],
            StringComparer.Ordinal
        );
    }

    /// <summary>Records each edit as the person's answer; returns how many changed something.</summary>
    public int Record(IEnumerable<LiveEdit> edits)
    {
        var changed = 0;

        // Removals first, so a swap of one value for another ends on the new one.
        foreach (
            var edit in edits
                .Distinct()
                .OrderBy(edit => edit.Removed ? 0 : 1)
                .ThenBy(edit => edit.Keyword, StringComparer.Ordinal)
        )
            if (edit.Removed ? Remove(edit) : Add(edit))
                changed++;

        return changed;
    }

    private bool Add(LiveEdit edit)
    {
        var parts = edit.Keyword.Split('|');

        switch (parts)
        {
            case [GenreVocabulary.Group, var genre] when GenreVocabulary.IsName(genre):
                if (
                    genres.Read().GetValueOrDefault(edit.Song) is { } held
                    && held.Genres.Contains(genre)
                    && !held.Rejected.Contains(genre)
                )
                    return false;

                genres.Choose(edit.Song, genre);

                return true;

            case [FamiliarityVocabulary.Group, var value] when FamiliarityVocabulary.IsValue(value):
                // Until crateside has written the song's familiarity, a value on its files may be one
                // an earlier lookup wrote.
                if (
                    familiarity.Read().GetValueOrDefault(edit.Song) is not { Tagged: [_, ..] } reach
                    || reach.Settled == value
                )
                    return false;

                familiarity.Settle(edit.Song, value);

                return true;

            default:
                if (
                    !VocalVocabulary.TryParseKeyword(edit.Keyword, out var vocal)
                    || vocals.Read().GetValueOrDefault(edit.Stem) == vocal
                )
                    return false;

                vocals.Settle(edit.Stem, vocal);

                return true;
        }
    }

    private bool Remove(LiveEdit edit)
    {
        var parts = edit.Keyword.Split('|');

        switch (parts)
        {
            case [GenreVocabulary.Group, var genre]:
                if (
                    genres.Read().GetValueOrDefault(edit.Song) is { } held
                    && held.RemovalsSeen.Contains(edit.Removal)
                )
                    return false;

                genres.Reject(edit.Song, genre, edit.Removal);

                return true;

            case [FamiliarityVocabulary.Group, var value]:
                if (
                    familiarity.Read().GetValueOrDefault(edit.Song) is { } reach
                    && reach.RemovalsSeen.Contains(edit.Removal)
                )
                    return false;

                familiarity.Reject(edit.Song, value, edit.Removal);

                return true;

            default:
                if (
                    !VocalVocabulary.TryParseKeyword(edit.Keyword, out var vocal)
                    || vocals.Read().GetValueOrDefault(edit.Stem) != vocal
                )
                    return false;

                vocals.Clear(edit.Stem);

                return true;
        }
    }
}
