using Crateside.Core.Familiarity;

namespace Crateside.Tests.Familiarity;

/// <summary>The two-vote rule on the 52 songs the note of 2026-11-09 measured (ADR 0089).</summary>
public sealed class FamiliarityNoteTests
{
    // The note's table, with its fitted cuts of 3,481 listeners and 20,461 views: label, then value.
    private static readonly Dictionary<(bool Known, string Value), int> NoteTable = new()
    {
        [(true, FamiliarityVocabulary.Mainstream)] = 15,
        [(true, FamiliarityVocabulary.Niche)] = 4,
        [(true, FamiliarityVocabulary.Obscure)] = 3,
        [(false, FamiliarityVocabulary.Mainstream)] = 1,
        [(false, FamiliarityVocabulary.Niche)] = 2,
        [(false, FamiliarityVocabulary.Obscure)] = 27,
    };

    internal static Dictionary<(bool Known, string Value), int> Spread(
        IEnumerable<(bool Known, string Value)> reads
    ) => reads.GroupBy(read => read).ToDictionary(group => group.Key, group => group.Count());

    [Fact]
    public void The_notes_numbers_give_its_table_within_the_one_song_the_round_cuts_move()
    {
        var spread = Spread(
            LabelledSong.All.Select(song =>
                (song.Known, FamiliarityVocabulary.FromReach(song.Listeners, song.Pageviews))
            )
        );

        Assert.Equal(52, spread.Values.Sum());
        Assert.All(
            NoteTable,
            cell =>
                Assert.InRange(spread.GetValueOrDefault(cell.Key), cell.Value - 1, cell.Value + 1)
        );
    }

    [Fact]
    public void The_known_songs_land_mostly_on_mainstream_and_the_unknown_mostly_on_obscure()
    {
        var spread = Spread(
            LabelledSong.All.Select(song =>
                (song.Known, FamiliarityVocabulary.FromReach(song.Listeners, song.Pageviews))
            )
        );

        Assert.Equal(15, spread[(true, FamiliarityVocabulary.Mainstream)]);
        Assert.Equal(27, spread[(false, FamiliarityVocabulary.Obscure)]);
    }
}
