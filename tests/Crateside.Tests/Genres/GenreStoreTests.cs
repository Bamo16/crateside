using Crateside.Core.Genres;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Genres;

public sealed class GenreStoreTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private static readonly DateTimeOffset At = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        _ledger.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_suggestion_is_the_first_label_that_maps_onto_the_list()
    {
        var store = new GenreStore(_ledger);

        store.Suggest("fox stevenson - glue gun", ["Dance", "drum and bass", "electronic"], At);

        var record = new GenreStore(_ledger).Read()["fox stevenson - glue gun"];

        Assert.Equal("Drum & Bass", record.Suggested);
        Assert.Equal(["Dance", "drum and bass", "electronic"], record.Heard);
        Assert.False(record.IsSettled);
        Assert.Null(record.Written);
    }

    [Fact]
    public void Only_a_settled_genre_is_written()
    {
        var store = new GenreStore(_ledger);

        store.Suggest("xg - woke up", ["Hip-Hop/Rap"], At);
        store.Settle("xg - woke up", "K-Pop");

        var record = new GenreStore(_ledger).Read()["xg - woke up"];

        Assert.Equal("Hip Hop", record.Suggested);
        Assert.Equal("K-Pop", record.Written);
    }

    [Fact]
    public void Settling_on_none_of_the_list_is_an_answer_too()
    {
        var store = new GenreStore(_ledger);

        store.Suggest("goreshit - toromi hearts 2", ["Breakbeat", "breakcore"], At);
        store.Settle("goreshit - toromi hearts 2", null);

        var record = store.Read()["goreshit - toromi hearts 2"];

        Assert.True(record.IsSettled);
        Assert.Null(record.Written);
    }

    [Fact]
    public void A_later_lookup_leaves_the_users_answer_alone()
    {
        var store = new GenreStore(_ledger);

        store.Settle("song", "Rock");
        store.Suggest("song", ["Pop"], At);

        Assert.Equal("Rock", store.Read()["song"].Written);
    }

    [Fact]
    public void A_genre_off_the_list_is_refused()
    {
        var store = new GenreStore(_ledger);

        Assert.Throws<ArgumentException>(() => store.Settle("song", "Breakcore"));
        Assert.Empty(store.Read());
    }
}
