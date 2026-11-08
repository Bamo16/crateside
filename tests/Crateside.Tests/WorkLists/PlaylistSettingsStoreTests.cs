using Crateside.Core.Ledger;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.WorkLists;

public sealed class PlaylistSettingsStoreTests
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    /// <summary>A new store over the same ledger is a restart.</summary>
    private PlaylistSettingsStore Playlist(string? configuredPlaylistId = null) =>
        new(_ledger, Options.Create(new PlaylistOptions { PlaylistId = configuredPlaylistId }));

    [Fact]
    public void Falls_back_to_configuration_before_anything_is_saved()
    {
        Assert.Equal("PL_FROM_CONFIG", Playlist("PL_FROM_CONFIG").Read().PlaylistId);
    }

    [Fact]
    public void A_saved_playlist_overrides_configuration_and_survives_a_restart()
    {
        Playlist("PL_FROM_CONFIG").SavePlaylistId("PL_FROM_USER");

        Assert.Equal("PL_FROM_USER", Playlist("PL_FROM_CONFIG").Read().PlaylistId);
    }

    [Fact]
    public void Saving_a_pasted_url_stores_the_id()
    {
        var store = Playlist();

        store.SavePlaylistId("https://www.youtube.com/playlist?list=PL123456");

        Assert.Equal("PL123456", store.Read().PlaylistId);
    }

    [Fact]
    public void Clearing_the_playlist_leaves_nothing_configured()
    {
        var store = Playlist();
        store.SavePlaylistId("PL123456");

        store.SavePlaylistId("");

        Assert.Null(store.Read().PlaylistId);
    }

    [Fact]
    public void Saving_the_playlist_does_not_drop_the_instructions()
    {
        new TrackInstructionStore(_ledger).SetName("abc123", "Starjunk 95 - 99th Street");

        Playlist().SavePlaylistId("PL_something");

        Assert.Equal("PL_something", Playlist().Read().PlaylistId);
        Assert.Single(new TrackInstructionStore(_ledger).Read());
    }

    [Fact]
    public void One_setting_saved_leaves_the_others_as_they_were()
    {
        var playlist = Playlist();

        playlist.SaveRefreshMinutes(11);
        playlist.SaveConsumeOnArrival(true);
        playlist.SavePlaylistId("PL123456");

        var settings = Playlist().Read();

        Assert.Equal(11, settings.RefreshMinutes);
        Assert.True(settings.ConsumeOnArrival);
        Assert.Equal("PL123456", settings.PlaylistId);
    }

    [Fact]
    public void Turning_consume_on_arrival_off_again_is_kept()
    {
        var playlist = Playlist();
        playlist.SaveConsumeOnArrival(true);

        playlist.SaveConsumeOnArrival(false);

        Assert.False(Playlist().Read().ConsumeOnArrival);
    }
}
