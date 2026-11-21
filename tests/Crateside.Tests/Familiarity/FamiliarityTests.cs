using Crateside.Core.Familiarity;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Familiarity;

public sealed class FamiliarityVocabularyTests
{
    [Theory]
    [InlineData(3_499, 19_999, "Obscure")]
    [InlineData(3_500, 19_999, "Niche")]
    [InlineData(3_499, 20_000, "Niche")]
    [InlineData(3_500, 20_000, "Mainstream")]
    [InlineData(234_367, 119_530, "Mainstream")] // Adele, Rolling in the Deep
    [InlineData(48_401, 76_640, "Mainstream")] // Bomfunk MC's, Freestyler
    [InlineData(1_660, 6_325, "Obscure")] // aespa, Spicy
    [InlineData(null, null, "Obscure")] // no recording and no article
    [InlineData(null, 20_000, "Niche")] // an article, no recording
    [InlineData(3_500, 0, "Niche")] // a recording, no article
    public void Two_votes_make_three_values(int? listeners, int? pageviews, string expected) =>
        Assert.Equal(expected, FamiliarityVocabulary.FromReach(listeners, pageviews));
}

public sealed class FamiliarityStoreTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    public void Dispose()
    {
        _ledger.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_measured_song_is_written_as_what_the_two_votes_say()
    {
        new FamiliarityStore(_ledger).Measure(
            "song",
            new FamiliarityReading(8_248, 289_222, "Q21172725"),
            DateTimeOffset.UtcNow,
            FamiliarityLookup.Version
        );

        var record = new FamiliarityStore(_ledger).Read()["song"];

        Assert.Equal(
            (8_248, 289_222, "Q21172725"),
            (record.Listeners, record.Pageviews, record.WikidataItem)
        );
        Assert.Equal("Mainstream", record.Written);
    }

    [Fact]
    public void A_song_the_sources_know_nothing_of_still_reads_obscure()
    {
        var store = new FamiliarityStore(_ledger);

        store.Measure(
            "song",
            new FamiliarityReading(null, 0, null),
            DateTimeOffset.UtcNow,
            FamiliarityLookup.Version
        );

        Assert.Equal("Obscure", store.Read()["song"].Written);
    }

    [Fact]
    public void A_song_nothing_has_looked_up_writes_nothing() =>
        Assert.Null(new FamiliarityRecord { Song = "unasked" }.Written);

    [Fact]
    public void The_users_answer_outranks_the_numbers_and_survives_a_new_measurement()
    {
        var store = new FamiliarityStore(_ledger);

        store.Settle("song", "Mainstream");
        store.Measure(
            "song",
            new FamiliarityReading(10, 0, null),
            DateTimeOffset.UtcNow,
            FamiliarityLookup.Version
        );

        Assert.Equal("Mainstream", new FamiliarityStore(_ledger).Read()["song"].Written);
    }

    [Fact]
    public void A_song_an_older_lookup_read_is_owed_another_but_a_current_one_is_not()
    {
        var store = new FamiliarityStore(_ledger);

        store.Measure(
            "old",
            new FamiliarityReading(1, 1, null),
            DateTimeOffset.UtcNow,
            version: FamiliarityLookup.Version - 1
        );
        store.Measure(
            "current",
            new FamiliarityReading(1, 1, null),
            DateTimeOffset.UtcNow,
            FamiliarityLookup.Version
        );

        Assert.True(store.Read()["old"].IsOwedALookup);
        Assert.False(store.Read()["current"].IsOwedALookup);
        Assert.True(new FamiliarityRecord { Song = "unasked" }.IsOwedALookup);
    }

    [Theory]
    [InlineData("Mainstream")]
    [InlineData("Niche")]
    [InlineData("Obscure")]
    public void Each_of_the_three_values_can_be_settled(string value)
    {
        var store = new FamiliarityStore(_ledger);

        store.Settle("song", value);

        Assert.Equal(value, store.Read()["song"].Settled);
    }

    [Fact]
    public void Settling_anything_but_the_three_values_is_refused() =>
        Assert.Throws<ArgumentException>(() =>
            new FamiliarityStore(_ledger).Settle("song", "Famous")
        );
}
