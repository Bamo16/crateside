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
    public void Saving_the_playlist_does_not_drop_the_instructions()
    {
        var store = Store();
        store.SetPlan("abc123", StemPlan.InstrumentalRelease);

        store.SavePlaylistId("PL_something");

        var reloaded = Store().Read();
        Assert.Equal("PL_something", reloaded.PlaylistId);
        Assert.Single(reloaded.Tracks);
    }

    [Fact]
    public void An_instruction_that_says_nothing_is_not_kept()
    {
        var store = Store();
        store.SetPlan("abc123", StemPlan.InstrumentalRelease);

        store.SetPlan("abc123", null);

        Assert.Empty(store.Read().Tracks);
    }

    [Fact]
    public void A_name_on_its_own_is_enough_to_keep_an_instruction()
    {
        // SaysSomething decides whether the entry is stored at all.
        var store = Store();

        store.SetName("abc123", "Starjunk 95 - 99th Street");

        var track = Assert.Single(store.Read().Tracks);
        Assert.Equal("Starjunk 95 - 99th Street", track.Name);
        Assert.Null(track.Plan);
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
    public void Clearing_a_name_keeps_another_instruction_that_was_also_stated()
    {
        var store = Store();
        store.SetPlan("abc123", StemPlan.InstrumentalRelease);
        store.SetName("abc123", "Starjunk 95 - 99th Street");

        store.SetName("abc123", null);

        var track = Assert.Single(store.Read().Tracks);
        Assert.True(track.Plan?.AsksTheSameAs(StemPlan.InstrumentalRelease));
        Assert.Null(track.Name);
    }

    [Fact]
    public void A_file_still_carrying_the_removed_hold_fields_reads_without_error()
    {
        // A queue.json from before holding was removed still carries a top-level `held` array.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            """
            { "playlistId": "PL_old", "held": [ { "videoId": "abc123", "reason": "wrong upload" } ] }
            """
        );

        var settings = Store().Read();

        Assert.Equal("PL_old", settings.PlaylistId);
        Assert.Empty(settings.Tracks);
    }

    [Fact]
    public void An_instruction_carrying_the_removed_hold_fields_keeps_the_rest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            """
            { "tracks": [ { "videoId": "abc123", "held": true, "holdReason": "x",
                            "name": "Starjunk 95 - 99th Street" } ] }
            """
        );

        var track = Assert.Single(Store().Read().Tracks);

        Assert.Equal("Starjunk 95 - 99th Street", track.Name);
    }

    [Fact]
    public void An_edit_made_outside_the_process_is_picked_up()
    {
        var store = Store();

        store.SaveConsumeOnArrival(false);
        EditOutside("""{ "consumeOnArrival": true }""");

        Assert.True(store.Read().ConsumeOnArrival);
    }

    [Fact]
    public void An_edit_made_outside_the_process_is_not_reverted_by_the_next_save()
    {
        var store = Store();

        store.SaveConsumeOnArrival(false);
        EditOutside("""{ "consumeOnArrival": true }""");

        // Saving anything read-applies-writes the whole file.
        store.SetName("abc123", "Starjunk 95 - 99th Street");

        Assert.True(store.Read().ConsumeOnArrival);
    }

    [Fact]
    public void A_save_on_a_fresh_store_keeps_settings_it_is_not_changing()
    {
        var store = Store();

        store.SaveRefreshMinutes(11);
        store.SaveConsumeOnArrival(true);

        store.SetName("abc123", "Starjunk 95 - 99th Street");

        var settings = store.Read();

        Assert.Equal(11, settings.RefreshMinutes);
        Assert.True(settings.ConsumeOnArrival);
    }

    private void EditOutside(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, json);

        // Two writes inside the filesystem's resolution are one timestamp.
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddSeconds(1));
    }
}
