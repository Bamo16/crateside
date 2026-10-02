using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Playlist;

/// <summary>A track crateside has taken on stays reachable after the playlist lets it go (ADR 0021).</summary>
public sealed class IntakeReachabilityTests : IDisposable
{
    private const string Gone = "RzQRxV2XqFU";
    private const string Held = "x3U22KHsrqE";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public IntakeReachabilityTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private IntakeStore Store() => new(Path.Combine(_directory, "intake.json"));

    private static WorkItem Entry(string videoId, string title, int addedDay = 1) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = title,
            AddedAt = DateTimeOffset.Parse($"2026-09-0{addedDay}T00:00:00+00:00"),
        };

    [Fact]
    public void A_recorded_track_is_in_the_list_though_the_playlist_is_empty()
    {
        var store = Store();

        store.Record([Entry(Gone, "Eminem - Medicine Ball")]);

        var item = Assert.Single(IntakeStore.Merge([], store.Read()));

        Assert.Equal(Gone, item.VideoId);
        Assert.Equal("Eminem - Medicine Ball", item.Title);
        Assert.Null(item.PlaylistItemId);
    }

    [Fact]
    public void A_track_still_in_the_playlist_keeps_its_entry()
    {
        var store = Store();

        store.Record([Entry(Held, "Still Queued")]);

        var item = Assert.Single(IntakeStore.Merge([Entry(Held, "Still Queued")], store.Read()));

        Assert.Equal($"PLI-{Held}", item.PlaylistItemId);
    }

    [Fact]
    public void A_track_appears_once_however_many_ways_it_is_known()
    {
        var store = Store();

        store.Record([Entry(Held, "Still Queued"), Entry(Gone, "Let Go")]);

        var merged = IntakeStore.Merge([Entry(Held, "Still Queued")], store.Read());

        Assert.Equal(2, merged.Count);
        Assert.Equal([Held, Gone], merged.Select(item => item.VideoId));
    }

    [Fact]
    public void A_track_known_only_from_its_plan_is_backfilled()
    {
        var store = Store();

        store.Backfill([Gone]);

        Assert.Equal(Gone, Assert.Single(IntakeStore.Merge([], store.Read())).VideoId);
    }

    [Fact]
    public void What_the_playlist_still_holds_comes_first()
    {
        var store = Store();

        store.Record([Entry(Gone, "Let Go", addedDay: 1)]);

        var merged = IntakeStore.Merge([Entry(Held, "Still Queued", addedDay: 9)], store.Read());

        Assert.Equal([Held, Gone], merged.Select(item => item.VideoId));
    }
}
