using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Queue;

public class QueueSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "queue.json"
    );

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private QueueSettingsStore Store(string? configuredPlaylistId = null) =>
        new(Options.Create(new QueueOptions { PlaylistId = configuredPlaylistId }), _path);

    [Fact]
    public void Falls_back_to_configuration_before_anything_is_saved()
    {
        Assert.Equal("PL_FROM_CONFIG", Store("PL_FROM_CONFIG").Read().PlaylistId);
    }

    [Fact]
    public void A_saved_playlist_overrides_configuration_and_survives_a_restart()
    {
        Store("PL_FROM_CONFIG").SavePlaylistId("PL_FROM_USER");

        // A fresh store is what the next run gets.
        Assert.Equal("PL_FROM_USER", Store("PL_FROM_CONFIG").Read().PlaylistId);
    }

    [Fact]
    public void Saving_a_pasted_url_stores_the_id()
    {
        var store = Store();

        store.SavePlaylistId("https://www.youtube.com/playlist?list=PL123456");

        Assert.Equal("PL123456", store.Read().PlaylistId);
    }

    [Fact]
    public void The_settings_file_holds_no_secret()
    {
        Store().SavePlaylistId("PL123456");

        var contents = File.ReadAllText(_path);

        Assert.Contains("PL123456", contents);
        Assert.DoesNotContain("apiKey", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_corrupt_settings_file_degrades_to_configuration()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json");

        Assert.Equal("PL_FROM_CONFIG", Store("PL_FROM_CONFIG").Read().PlaylistId);
    }

    [Fact]
    public void Clearing_the_playlist_leaves_nothing_configured()
    {
        var store = Store();
        store.SavePlaylistId("PL123456");

        store.SavePlaylistId("");

        Assert.Null(store.Read().PlaylistId);
    }

    [Fact]
    public void A_hold_survives_a_restart()
    {
        Store().Hold("abc123", "wrong upload — want the album version");

        var reloaded = Store().Read();

        Assert.Equal("abc123", Assert.Single(reloaded.Held).VideoId);
        Assert.Equal("wrong upload — want the album version", reloaded.Held[0].Reason);
    }

    [Fact]
    public void Saving_the_playlist_does_not_drop_the_holds()
    {
        // Every mutator used to write a freshly-constructed settings object, so saving one field
        // discarded the others. Harmless while there was only one field, and not any more.
        var store = Store();
        store.Hold("abc123");

        store.SavePlaylistId("PL_something");

        var reloaded = Store().Read();
        Assert.Equal("PL_something", reloaded.PlaylistId);
        Assert.Single(reloaded.Held);
    }

    [Fact]
    public void Holding_the_same_track_twice_holds_it_once()
    {
        var store = Store();

        store.Hold("abc123", "first");
        store.Hold("abc123", "second");

        Assert.Equal("second", Assert.Single(store.Read().Held).Reason);
    }

    [Fact]
    public void Releasing_removes_only_that_track()
    {
        var store = Store();
        store.Hold("abc123");
        store.Hold("def456");

        store.Release("abc123");

        Assert.Equal("def456", Assert.Single(store.Read().Held).VideoId);
    }

    [Fact]
    public void Releasing_something_that_was_never_held_is_not_an_error() =>
        Assert.Empty(Store().Release("never-held").Held);
}
