using Crateside.Core.Genres;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Genres;

/// <summary>The table against the labels the ledger's lookups actually kept, on a snapshot of this machine's ledger.</summary>
public sealed class GenreMappingRealDataTests
{
    /// <summary>Labels that describe what a recording is for, not what it sounds like.</summary>
    private static readonly string[] NotGenres = ["soundtrack", "comedy", "instrumental", "ballad"];

    private static IReadOnlyList<GenreRecord> Records()
    {
        Assert.SkipUnless(TestLedger.HasReal, "No ledger.db on this machine.");

        using var ledger = TestLedger.StoreReal();

        return [.. new GenreStore(ledger).Read().Values.Where(record => record.Heard.Count > 0)];
    }

    [Fact]
    public void Every_song_with_a_label_the_table_maps_gets_one_or_two_of_the_29()
    {
        var records = Records();
        var mapped = records
            .Where(record => record.Heard.Any(label => GenreMapping.Map(label).Count > 0))
            .ToList();
        var none = records.Count - mapped.Count;

        TestContext.Current.SendDiagnosticMessage(
            $"{mapped.Count} of {records.Count} songs with source labels get a genre; {none} get none"
        );

        Assert.NotEmpty(mapped);
        Assert.All(
            mapped,
            record =>
            {
                Assert.InRange(record.FromSources.Count, 1, 2);
                Assert.All(
                    record.FromSources,
                    genre => Assert.True(GenreVocabulary.IsGenre(genre))
                );
            }
        );
        Assert.All(records.Except(mapped), record => Assert.Empty(record.FromSources));
    }

    [Fact]
    public void Every_label_on_three_or_more_songs_is_in_the_table_unless_it_names_no_genre()
    {
        var unmapped = Records()
            .SelectMany(record => record.Heard.Select(GenreMapping.Normalise).Distinct())
            .GroupBy(label => label)
            .Where(label => label.Count() >= 3 && GenreMapping.Map(label.Key).Count is 0)
            .Select(label => label.Key)
            .Order()
            .ToList();

        Assert.Equal([.. NotGenres.Order()], unmapped);
    }

    [Fact]
    public void The_migration_leaves_no_answer_and_no_learned_genre_on_a_snapshot_of_the_real_ledger()
    {
        Assert.SkipUnless(TestLedger.HasReal, "No ledger.db on this machine.");

        using var ledger = TestLedger.StoreReal();
        var records = new GenreStore(ledger).Read().Values;

        Assert.All(
            records,
            record =>
            {
                Assert.False(record.IsTheUsersAnswer);
                Assert.Null(record.LearnedFrom);
                Assert.Null(record.LookedUpAt);
                Assert.Empty(record.Mine);
            }
        );
    }
}
