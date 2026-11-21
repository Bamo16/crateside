using Crateside.Core.Ledger;
using Crateside.Core.Marks;

namespace Crateside.Tests.Marks;

public sealed class MeasuringExclusionStoreTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    public void Dispose()
    {
        _ledger.Dispose();
    }

    private static MeasuringStretch Stretch(double start, double end, string typed = "1–2") =>
        new()
        {
            StartSeconds = start,
            EndSeconds = end,
            Typed = typed,
            Reason = "slows down",
        };

    [Fact]
    public void A_stretch_survives_a_restart_in_seconds_with_what_was_typed_and_why()
    {
        new MeasuringExclusionStore(_ledger).AddStretch(
            "artist - title",
            Stretch(364, 380, "183–191")
        );

        var held = new MeasuringExclusionStore(_ledger).Of("artist - title");

        Assert.NotNull(held);
        Assert.False(held.WholeTrack);
        Assert.Equal([Stretch(364, 380, "183–191")], held.Stretches);
    }

    [Fact]
    public void Marking_the_whole_track_replaces_its_stretches_and_keeps_the_reason()
    {
        var store = new MeasuringExclusionStore(_ledger);
        store.AddStretch("song", Stretch(1, 2));

        store.MarkWholeTrack("song", "live recording");

        var held = new MeasuringExclusionStore(_ledger).Of("song");
        Assert.NotNull(held);
        Assert.True(held.WholeTrack);
        Assert.Equal("live recording", held.Reason);
        Assert.Empty(held.Stretches);
    }

    [Fact]
    public void Adding_a_stretch_to_a_marked_track_leaves_only_the_stretches()
    {
        var store = new MeasuringExclusionStore(_ledger);
        store.MarkWholeTrack("song", "live recording");

        store.AddStretch("song", Stretch(1, 2));

        Assert.False(new MeasuringExclusionStore(_ledger).Of("song")?.WholeTrack);
    }

    [Fact]
    public void Stretches_come_back_in_order_of_start()
    {
        var store = new MeasuringExclusionStore(_ledger);
        store.AddStretch("song", Stretch(50, 60));
        store.AddStretch("song", Stretch(10, 20));

        Assert.Equal(
            [10d, 50d],
            new MeasuringExclusionStore(_ledger)
                .Of("song")!
                .Stretches.Select(each => each.StartSeconds)
        );
    }

    [Fact]
    public void Removing_the_last_stretch_forgets_the_song()
    {
        var store = new MeasuringExclusionStore(_ledger);
        store.AddStretch("song", Stretch(10, 20));

        store.RemoveStretch("song", Stretch(10, 20));

        Assert.Empty(new MeasuringExclusionStore(_ledger).Read());
    }

    [Fact]
    public void Clearing_forgets_the_song()
    {
        var store = new MeasuringExclusionStore(_ledger);
        store.MarkWholeTrack("song", "live recording");

        store.Clear("song");

        Assert.Null(new MeasuringExclusionStore(_ledger).Of("song"));
    }

    [Fact]
    public void Songs_are_found_whatever_the_case_they_were_marked_in()
    {
        new MeasuringExclusionStore(_ledger).MarkWholeTrack("Artist - Title", "x");

        Assert.NotNull(new MeasuringExclusionStore(_ledger).Of("artist - title"));
    }

    [Fact]
    public void A_mark_announces_the_song_it_was_set_on()
    {
        var store = new MeasuringExclusionStore(_ledger);
        var announced = new List<string>();
        store.Changed += announced.Add;

        store.MarkWholeTrack("song", "x");

        Assert.Equal(["song"], announced);
    }

    [Fact]
    public void A_mark_filed_under_a_video_ID_follows_the_song_it_is_judged_as()
    {
        var store = new MeasuringExclusionStore(_ledger);
        store.AddStretch("abc123", Stretch(10, 20));

        var moved = store.FollowSongs(key => key == "abc123" ? "artist - title" : null);

        Assert.Equal(1, moved);

        var reread = new MeasuringExclusionStore(_ledger);
        Assert.Null(reread.Of("abc123"));
        Assert.Equal([Stretch(10, 20)], reread.Of("artist - title")!.Stretches);
    }
}
