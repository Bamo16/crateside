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

    [Fact]
    public void Forgetting_a_track_drops_its_record()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);
        store.Forget(Track);

        Assert.Equal(Other, Assert.Single(store.Read()).VideoId);
    }
}
