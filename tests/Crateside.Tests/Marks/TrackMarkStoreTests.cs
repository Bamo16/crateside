using Crateside.Core.Ledger;
using Crateside.Core.Marks;

namespace Crateside.Tests.Marks;

public sealed class TrackMarkStoreTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    public void Dispose()
    {
        _ledger.Dispose();
    }

    [Fact]
    public void A_mark_survives_a_restart()
    {
        new TrackMarkStore(_ledger).Set("artist - title", TrackMark.ChangesKey, true);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(_ledger).Of("artist - title"));
    }

    [Fact]
    public void Songs_are_found_whatever_the_case_they_were_marked_in()
    {
        new TrackMarkStore(_ledger).Set("Artist - Title", TrackMark.ChangesKey, true);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(_ledger).Of("artist - title"));
    }

    [Fact]
    public void A_mark_announces_the_song_it_was_set_on()
    {
        var store = new TrackMarkStore(_ledger);
        var announced = new List<string>();
        store.Changed += announced.Add;

        store.Set("artist - title", TrackMark.OddBars, true);

        Assert.Equal(["artist - title"], announced);
    }

    [Fact]
    public void Clearing_the_last_mark_forgets_the_song()
    {
        var store = new TrackMarkStore(_ledger);
        store.Set("song", TrackMark.ChangesKey, true);

        store.Set("song", TrackMark.ChangesKey, false);

        Assert.Empty(new TrackMarkStore(_ledger).Read());
        Assert.Empty(new TrackMarkStore(_ledger).Of("song"));
    }

    [Fact]
    public void Each_mark_is_set_and_cleared_on_its_own()
    {
        var store = new TrackMarkStore(_ledger);
        store.Set("song", TrackMark.OddBars, true);
        store.Set("song", TrackMark.ChangesKey, true);

        store.Set("song", TrackMark.OddBars, false);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(_ledger).Of("song"));
    }

    [Fact]
    public void Marking_twice_records_it_once()
    {
        var store = new TrackMarkStore(_ledger);

        store.Set("song", TrackMark.ChangesKey, true);
        store.Set("song", TrackMark.ChangesKey, true);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(_ledger).Of("song"));
    }
}
