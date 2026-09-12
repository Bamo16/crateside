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
    public void Saving_the_playlist_does_not_drop_the_instructions()
    {
        // Every mutator used to write a freshly-constructed settings object, so saving one field
        // discarded the others. Harmless while there was only one field, and not any more.
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
        // Otherwise every track ever spoken about leaves a permanent line in the file, and the
        // list stops meaning "things I have said something about".
        var store = Store();
        store.SetPlan("abc123", StemPlan.InstrumentalRelease);

        store.SetPlan("abc123", null);

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
        // Holding was removed, and a queue.json written before that still carries it — in the
        // oldest shape, a top-level `held` array. Unknown properties are skipped rather than
        // refused, so everything else in the file survives and the next write drops them.
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
}
