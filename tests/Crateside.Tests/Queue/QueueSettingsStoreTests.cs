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

        var reloaded = Assert.Single(Store().Read().Tracks);

        Assert.Equal("abc123", reloaded.VideoId);
        Assert.True(reloaded.IsHeld);
        Assert.Equal("wrong upload — want the album version", reloaded.HoldReason);
    }

    [Fact]
    public void Saving_the_playlist_does_not_drop_the_instructions()
    {
        // Every mutator used to write a freshly-constructed settings object, so saving one field
        // discarded the others. Harmless while there was only one field, and not any more.
        var store = Store();
        store.Hold("abc123");

        store.SavePlaylistId("PL_something");

        var reloaded = Store().Read();
        Assert.Equal("PL_something", reloaded.PlaylistId);
        Assert.Single(reloaded.Tracks);
    }

    [Fact]
    public void Holding_the_same_track_twice_holds_it_once()
    {
        var store = Store();

        store.Hold("abc123", "first");
        store.Hold("abc123", "second");

        Assert.Equal("second", Assert.Single(store.Read().Tracks).HoldReason);
    }

    [Fact]
    public void Releasing_removes_only_that_track()
    {
        var store = Store();
        store.Hold("abc123");
        store.Hold("def456");

        store.Release("abc123");

        Assert.Equal("def456", Assert.Single(store.Read().Tracks).VideoId);
    }

    [Fact]
    public void Releasing_something_that_was_never_held_is_not_an_error() =>
        Assert.Empty(Store().Release("never-held").Tracks);

    [Fact]
    public void An_instruction_that_says_nothing_is_not_kept()
    {
        // Otherwise every track ever held leaves a permanent line in the file, and the list
        // stops meaning "things I have said something about".
        var store = Store();
        store.Hold("abc123");

        store.Release("abc123");

        Assert.Empty(store.Read().Tracks);
    }

    [Fact]
    public void A_name_on_its_own_is_enough_to_keep_an_instruction()
    {
        // SaysSomething is what decides whether the entry is stored at all, so a field left out
        // of it cannot be persisted — the name would be dropped the moment it was saved.
        var store = Store();

        store.SetName("abc123", "Starjunk 95 - 99th Street");

        var track = Assert.Single(store.Read().Tracks);
        Assert.Equal("Starjunk 95 - 99th Street", track.Name);
        Assert.False(track.IsHeld);
    }

    [Fact]
    public void A_name_survives_a_restart()
    {
        Store().SetName("abc123", "Starjunk 95 - 99th Street");

        Assert.Equal("Starjunk 95 - 99th Street", Assert.Single(Store().Read().Tracks).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clearing_a_name_drops_the_instruction_with_it(string? cleared)
    {
        // Absence is what "not stated" means, and blank is not a name anyone typed on purpose.
        var store = Store();
        store.SetName("abc123", "Starjunk 95 - 99th Street");

        store.SetName("abc123", cleared);

        Assert.Empty(store.Read().Tracks);
    }

    [Fact]
    public void A_name_is_stored_trimmed()
    {
        var store = Store();

        store.SetName("abc123", "  Starjunk 95 - 99th Street  ");

        Assert.Equal("Starjunk 95 - 99th Street", Assert.Single(store.Read().Tracks).Name);
    }

    [Fact]
    public void Clearing_a_name_keeps_a_hold_that_was_also_stated()
    {
        var store = Store();
        store.Hold("abc123", "not the canonical upload");
        store.SetName("abc123", "Starjunk 95 - 99th Street");

        store.SetName("abc123", null);

        var track = Assert.Single(store.Read().Tracks);
        Assert.True(track.IsHeld);
        Assert.Null(track.Name);
    }

    [Fact]
    public void An_instrumental_release_and_a_hold_are_one_instruction()
    {
        var store = Store();

        store.SetInstrumentalRelease("abc123", true);
        store.Hold("abc123", "not the canonical upload");

        var track = Assert.Single(store.Read().Tracks);
        Assert.True(track.IsInstrumentalRelease);
        Assert.True(track.IsHeld);
    }

    [Fact]
    public void Releasing_a_hold_keeps_the_instrumental_release_fact()
    {
        // The two are independent: a hold is a deferral, and whether the release has vocals
        // stays true whether or not crateside is currently allowed to work the track.
        var store = Store();
        store.SetInstrumentalRelease("abc123", true);
        store.Hold("abc123");

        store.Release("abc123");

        var track = Assert.Single(store.Read().Tracks);
        Assert.True(track.IsInstrumentalRelease);
        Assert.False(track.IsHeld);
    }

    [Fact]
    public void Holds_written_in_the_old_shape_are_still_read()
    {
        // queue.json predates instructions, and the holds in it are real user decisions. The
        // next write puts them in the new shape; nothing is lost in between.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            """
            { "playlistId": "PL_old", "held": [ { "videoId": "abc123", "reason": "wrong upload" } ] }
            """
        );

        var settings = Store().Read();

        Assert.Equal("PL_old", settings.PlaylistId);
        var track = Assert.Single(settings.Tracks);
        Assert.Equal("abc123", track.VideoId);
        Assert.True(track.IsHeld);
        Assert.Equal("wrong upload", track.HoldReason);
    }
}
