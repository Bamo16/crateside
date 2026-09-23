using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// What crateside keeps so it never has to ask the playlist again (ADR 0021).
/// </summary>
public class IntakeStoreTests : IDisposable
{
    private const string Track = "aaaaaaaaaaa";
    private const string Other = "bbbbbbbbbbb";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public IntakeStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private IntakeStore Store() => new(Path.Combine(_directory, "intake.json"));

    private static QueueItem Item(string videoId, string title = "Some Track") =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = title,
            AddedAt = DateTimeOffset.Parse("2026-09-15T12:00:00+00:00"),
            PremiumOutlook = PremiumOutlook.ArtTrack,
        };

    [Fact]
    public void A_track_survives_the_entry_it_arrived_on()
    {
        Store().Record([Item(Track, "Medicine Ball")]);

        var kept = Store().Read();

        Assert.Equal(Track, Assert.Single(kept).VideoId);
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

    /// <remarks>
    /// The entry is the weaker source once read: a title edited upstream, or a track the user has
    /// since renamed, must not be overwritten by a later scan.
    /// </remarks>
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
        Assert.All(store.Read(), record => Assert.Equal(record.VideoId, record.Title));
    }

    [Fact]
    public void Backfilling_never_displaces_a_real_record()
    {
        var store = Store();

        store.Record([Item(Track, "Medicine Ball")]);
        store.Backfill([Track]);

        Assert.Equal("Medicine Ball", Assert.Single(store.Read()).Title);
    }

    /// <summary>
    /// Nothing to delete upstream, which is what <see cref="QueueRemover"/> reads to refuse.
    /// </summary>
    [Fact]
    public void A_record_read_back_carries_no_playlist_entry()
    {
        Store().Record([Item(Track)]);

        Assert.Null(IntakeStore.AsQueueItem(Store().Read()[0]).PlaylistItemId);
    }

    [Fact]
    public void A_store_that_has_never_been_written_reads_as_empty() =>
        Assert.Empty(Store().Read());

    [Fact]
    public void An_unreadable_store_reads_as_empty_rather_than_throwing()
    {
        var path = Path.Combine(_directory, "intake.json");

        File.WriteAllText(path, "{ this is not json");

        Assert.Empty(new IntakeStore(path).Read());
    }

    /// <summary>
    /// What the playlist has delivered, which is every entry already recorded — not only the ones
    /// this scan recorded.
    /// </summary>
    /// <remarks>
    /// Consuming only what a scan reported as new means a setting turned on after the first scan
    /// never fires: nothing is new again, so nothing is ever given back.
    /// </remarks>
    [Fact]
    public void Everything_recorded_stays_recorded_for_a_later_scan_to_act_on()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);

        var recorded = Store().Read().Select(r => r.VideoId).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(Track, recorded);
        Assert.Contains(Other, recorded);
        Assert.Empty(Store().Record([Item(Track), Item(Other)]));
    }

    [Fact]
    public void A_store_that_has_written_cleanly_is_durable()
    {
        var store = Store();

        store.Record([Item(Track)]);

        Assert.True(store.IsDurable);
    }

    /// <remarks>
    /// The entry is the only other copy of the track, so a record that never reached disk must
    /// not be grounds for deleting it.
    /// </remarks>
    [Fact]
    public void A_store_that_cannot_write_is_not_durable()
    {
        var path = Path.Combine(_directory, "intake.json");

        // A directory where the file belongs: writing it fails the way a locked or read-only
        // store would, without needing either.
        Directory.CreateDirectory(path);

        var store = new IntakeStore(path);

        store.Record([Item(Track)]);

        Assert.False(store.IsDurable);
    }

    /// <summary>
    /// An entry is offered back once and never again, whatever the removal said.
    /// </summary>
    /// <remarks>
    /// Consumption had no backoff at all, and a delete costs fifty quota units. Failing removals
    /// were retried on every scan — every page read and every stage worker's two-minute loop —
    /// and spent a day's YouTube quota.
    /// </remarks>
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
    public void The_mark_survives_a_restart()
    {
        var store = Store();

        store.Record([Item(Track)]);
        store.MarkRemovalAttempted([Track], DateTimeOffset.UtcNow);

        Assert.Empty(Offerable(new IntakeStore(Path.Combine(_directory, "intake.json"))));
    }

    /// <summary>
    /// A lapsed token is refused before the first delete, so the mark cost those entries their one
    /// offer and bought nothing. Clearing it is what lets re-consent offer them again (#34).
    /// </summary>
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
        [.. store.Read().Where(r => r.RemovalAttemptedAt is null).Select(r => r.VideoId)];

    /// <summary>
    /// A stand-in the backfill should never have made is cleared.
    /// </summary>
    /// <remarks>
    /// Narrowing the backfill stops new ones; without this the ones already written sit in the
    /// line for the GPU for ever.
    /// </remarks>
    [Fact]
    public void A_stand_in_for_an_instruction_that_asks_nothing_is_pruned()
    {
        var store = Store();

        store.Backfill([Track, Other]);
        store.Prune(owed: new HashSet<string> { Other }, held: new HashSet<string>());

        Assert.Equal(Other, Assert.Single(store.Read()).VideoId);
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

        Assert.Equal(Other, Assert.Single(store.Read()).VideoId);
    }
}
