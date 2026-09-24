using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// A track whose plan asks for files that are not on disk says so, and claims nothing else
/// (ADR 0027).
/// </summary>
public class ShortTrackTests
{
    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"}
        ]
        """
    );

    /// <summary>
    /// Moe Shop - HARDCODED: the vocal staged and tagged, the instrumental never made. It read as
    /// ready to warp, and nothing on the row said a file was missing.
    /// </summary>
    [Fact]
    public void A_cut_track_missing_a_planned_preset_is_short_rather_than_ready_to_warp()
    {
        var row = Row(inLibrary: false, "Vocal - Full");

        Assert.Equal(TrackState.Short, row.State);
        Assert.Equal(["instrumental_full"], row.Shortfall.Presets);
        Assert.Equal("short · 1 file missing", row.ChipLabel);
    }

    [Fact]
    public void A_track_whose_files_all_reached_the_library_is_still_short_if_one_never_existed()
    {
        // Rob Zombie - Superbeast's shape (#38): everything present is in Mashup Samples, which
        // otherwise reads as done.
        Assert.Equal(TrackState.Short, Row(inLibrary: true, "Vocal - Full").State);
    }

    [Fact]
    public void A_track_with_everything_its_plan_asks_for_is_not_short()
    {
        var row = Row(inLibrary: false, "Vocal - Full", "Instrumental - Full");

        Assert.False(row.IsShort);
        Assert.Equal(TrackState.ReadyToWarp, row.State);
    }

    [Fact]
    public void A_missing_kept_source_alone_is_not_short()
    {
        // The download stage copies a kept source back on its own, so there is nothing to ask for.
        var row = Row(inLibrary: false, "Vocal - Full", "Instrumental - Full") with
        {
            Plan = StemPlan.Default with { KeepSource = true },
        };

        Assert.False(row.IsShort);
    }

    [Fact]
    public void A_track_never_cut_is_not_short()
    {
        var row = new TrackRow { VideoId = "_DiMKyZwaA0", Title = "Moe Shop - HARDCODED" };

        Assert.False(row.IsShort);
    }

    /// <summary>
    /// Measured on the real library, 2026-09-23: without this 16 of 393 rows read short, every one
    /// library-only — an acapella among them, told it was missing a vocal.
    /// </summary>
    [Fact]
    public void A_track_crateside_never_planned_is_not_short()
    {
        var row = Row(inLibrary: true, "Vocal - Full") with { IsLibraryOnly = true };

        Assert.False(row.IsShort);
        Assert.Equal(TrackState.Done, row.State);
    }

    [Fact]
    public void A_track_read_without_a_catalog_is_not_short()
    {
        // Every file then reads as no preset at all, so the whole plan would look missing.
        var row = Row(inLibrary: false, "Vocal - Full") with
        {
            Inventory = StemInventory.Of([], PresetCatalog.Empty),
        };

        Assert.False(row.IsShort);
    }

    [Fact]
    public void Being_parked_says_more_than_being_short()
    {
        var row = Row(inLibrary: false, "Vocal - Full") with { ParkedFor = ParkReason.NoKey };

        Assert.Equal(TrackState.Parked, row.State);
    }

    [Fact]
    public void A_short_track_needs_attention()
    {
        Assert.True(Row(inLibrary: false, "Vocal - Full").Matches(TrackFilter.NeedsAttention));
        Assert.False(Row(inLibrary: false, "Vocal - Full").Matches(TrackFilter.InStaging));
    }

    private static TrackRow Row(bool inLibrary, params string[] presets) =>
        new()
        {
            VideoId = "_DiMKyZwaA0",
            Title = "Moe Shop - HARDCODED",
            Plan = StemPlan.Default,
            Samples =
            [
                .. presets.Select(preset => new TrackSample
                {
                    Label = preset,
                    AbletonKey = "D# minor",
                    IsInLibrary = inLibrary,
                }),
            ],
            Inventory = StemInventory.Of(
                presets.Select(preset => new StemOnDisk
                {
                    Path = $@"P:\RYAN\Ableton\Sample Staging\Moe Shop - HARDCODED ({preset}).flac",
                    Root = @"P:\RYAN\Ableton\Sample Staging",
                    Preset = preset,
                }),
                Catalog
            ),
        };
}
