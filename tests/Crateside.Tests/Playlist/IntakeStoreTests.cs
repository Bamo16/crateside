using Crateside.Core.Imports;
using Crateside.Core.Ledger;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Playlist;

/// <summary>
/// What crateside keeps so it never has to ask the playlist again (ADR 0021).
/// </summary>
public sealed class IntakeStoreTests
{
    private const string Track = "aaaaaaaaaaa";
    private const string Other = "bbbbbbbbbbb";

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private IntakeStore Store() => new(_ledger);

    private static WorkItem Item(string trackKey, string title = "Some Track") =>
        new()
        {
            PlaylistItemId = $"PLI-{trackKey}",
            TrackKey = trackKey,
            Title = title,
            AddedAt = DateTimeOffset.Parse("2026-09-15T12:00:00+00:00"),
            PremiumOutlook = PremiumOutlook.ArtTrack,
        };

    [Fact]
    public void A_track_survives_the_entry_it_arrived_on()
    {
        Store().Record([Item(Track, "Medicine Ball")]);

        var kept = Store().Read();

        Assert.Equal(Track, Assert.Single(kept).TrackKey);
        Assert.Equal("Medicine Ball", kept[0].Title);
        Assert.Equal(PremiumOutlook.ArtTrack, kept[0].PremiumOutlook);
    }

    [Fact]
    public void Seeing_the_same_track_again_adds_nothing()
    {
        var store = Store();

        Assert.Single(store.Record([Item(Track)]));
        Assert.Empty(store.Record([Item(Track)]));
        Assert.Single(store.Read());
    }

    [Fact]
    public void A_later_scan_does_not_overwrite_what_was_recorded()
    {
        var store = Store();

        store.Record([Item(Track, "As First Seen")]);
        store.Record([Item(Track, "Retitled Upstream")]);

        Assert.Equal("As First Seen", Assert.Single(store.Read()).Title);
    }

    [Fact]
    public void A_track_known_only_from_the_settings_is_backfilled()
    {
        var store = Store();

        store.Backfill([Track, Other]);

        Assert.Equal(2, store.Read().Count);
        Assert.All(store.Read(), record => Assert.Equal(record.TrackKey, record.Title));
    }

    [Fact]
    public void Backfilling_never_displaces_a_real_record()
    {
        var store = Store();

        store.Record([Item(Track, "Medicine Ball")]);
        store.Backfill([Track]);

        Assert.Equal("Medicine Ball", Assert.Single(store.Read()).Title);
    }

    [Fact]
    public void A_record_read_back_carries_no_playlist_entry()
    {
        Store().Record([Item(Track)]);

        Assert.Null(IntakeStore.AsQueueItem(Store().Read()[0]).PlaylistItemId);
    }

    [Fact]
    public void A_store_that_has_never_been_written_reads_as_empty() =>
        Assert.Empty(Store().Read());

    /// <summary>
    /// What the playlist has delivered, which is every entry already recorded — not only the ones
    /// this scan recorded.
    /// </summary>
    [Fact]
    public void Everything_recorded_stays_recorded_for_a_later_scan_to_act_on()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);

        var recorded = Store().Read().Select(r => r.TrackKey).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(Track, recorded);
        Assert.Contains(Other, recorded);
        Assert.Empty(Store().Record([Item(Track), Item(Other)]));
    }

    [Fact]
    public void A_write_that_fails_throws_and_is_not_remembered()
    {
        using var ledger = new LedgerStore(LedgerStore.InMemory, TimeProvider.System);
        var store = new IntakeStore(ledger);

        store.Record([Item(Track)]);
        ledger.Dispose();

        Assert.ThrowsAny<Exception>(() => store.Record([Item(Other)]));
        Assert.Equal(Track, Assert.Single(store.Read()).TrackKey);
    }

    [Fact]
    public void Every_field_of_a_record_survives_a_restart()
    {
        var record = new IntakeRecord
        {
            TrackKey = "file~25edb68200c05e82",
            Title = "Dave Wise - Lockjaw's Saga",
            ChannelTitle = "Rare",
            AddedAt = DateTimeOffset.Parse("2026-11-06T20:19:06.3859908+00:00"),
            PremiumOutlook = PremiumOutlook.ArtTrack,
            IsUnavailable = true,
            PageUrl = "https://example.org/page",
            Artist = "Dave Wise",
            SongTitle = "Lockjaw's Saga",
            Release = new Release
            {
                Album = "Donkey Kong Country 2",
                Track = 6,
                Year = 1995,
                YearKind = YearKind.Release,
                Cover = "file~25edb68200c05e82.png",
            },
            RemovalAttemptedAt = DateTimeOffset.Parse("2026-11-07T01:00:00+00:00"),
            RemovalFailure = new RemovalFailure { Reason = "forbidden", CanSucceedLater = true },
            RemovedEntryId = "PLI-1",
        };

        Assert.True(Store().TakeIn(record));

        Assert.Equal(record, Assert.Single(Store().Read()));
    }

    [Fact]
    public void Records_read_back_in_the_order_they_arrived()
    {
        Store().Record([Item(Other), Item(Track)]);
        Store().MarkRemovalAttempted([Other], DateTimeOffset.UtcNow);

        Assert.Equal([Other, Track], Store().Read().Select(record => record.TrackKey));
    }

    [Fact]
    public void An_entry_offered_back_is_not_offered_again()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);

        Assert.All(store.Read(), record => Assert.Null(record.RemovalAttemptedAt));

        store.MarkRemovalAttempted([Track], DateTimeOffset.UtcNow);

        var offerable = Offerable(Store());

        Assert.Equal([Other], offerable);
    }

    [Fact]
    public async Task Scans_claiming_at_once_never_both_offer_the_same_entry()
    {
        var store = Store();
        WorkItem[] held = [Item(Track), Item(Other)];
        store.Record(held);

        var claims = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() => store.ClaimForRemoval(held, DateTimeOffset.UtcNow)))
        );

        Assert.Equal(
            [Track, Other],
            claims.SelectMany(claim => claim).Select(item => item.TrackKey).Order()
        );
    }

    [Fact]
    public void The_mark_survives_a_restart()
    {
        var store = Store();

        store.Record([Item(Track)]);
        store.MarkRemovalAttempted([Track], DateTimeOffset.UtcNow);

        Assert.Empty(Offerable(new IntakeStore(_ledger)));
    }

    [Fact]
    public void An_entry_a_lapse_never_offered_is_offered_again()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);
        store.MarkRemovalAttempted([Track, Other], DateTimeOffset.UtcNow);

        Assert.Empty(Offerable(store));

        store.ClearRemovalAttempted([Track]);

        Assert.Equal([Track], Offerable(Store()));
    }

    private static string[] Offerable(IntakeStore store) =>
        [.. store.Read().Where(r => r.RemovalAttemptedAt is null).Select(r => r.TrackKey)];

    [Fact]
    public void A_stand_in_for_an_instruction_that_asks_nothing_is_pruned()
    {
        var store = Store();

        store.Backfill([Track, Other]);
        store.Prune(owed: new HashSet<string> { Other }, held: new HashSet<string>());

        Assert.Equal(Other, Assert.Single(store.Read()).TrackKey);
    }

    [Fact]
    public void A_record_made_from_a_real_entry_is_never_pruned()
    {
        var store = Store();

        store.Record([Item(Track, "Some Track")]);
        store.Prune(owed: new HashSet<string>(), held: new HashSet<string>());

        Assert.Equal("Some Track", Assert.Single(store.Read()).Title);
    }

    [Fact]
    public void A_stand_in_for_a_track_the_playlist_still_holds_is_kept()
    {
        var store = Store();

        store.Backfill([Track]);
        store.Prune(owed: new HashSet<string>(), held: new HashSet<string> { Track });

        Assert.Single(store.Read());
    }

    [Fact]
    public void Forgetting_a_track_drops_its_record()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);
        store.Forget(Track);

        Assert.Equal(Other, Assert.Single(store.Read()).TrackKey);
    }
}
