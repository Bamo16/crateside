using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// The stem list as the picker. A line is either on disk or not and either ticked or not, and
/// those two crossing is the whole control.
/// </summary>
public class StemPickerTests
{
    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals",
           "description":"Maximum vocal capture"},
          {"id":"vocal_clean","displayName":"Vocal - Clean","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"},
          {"id":"instrumental_clean","displayName":"Instrumental - Clean","category":"Instrumentals"}
        ]
        """
    );

    private static TrackSample Made(string presetId, string label, bool inLibrary = false) =>
        new()
        {
            Label = label,
            Kind = StemKind.Preset,
            PresetId = presetId,
            IsInLibrary = inLibrary,
        };

    private static TrackRow Row(
        StemPlan? plan = null,
        bool hasWarpGuide = false,
        params TrackSample[] samples
    ) =>
        new()
        {
            VideoId = "aaaaaaaaaaa",
            Title = "Rob Dougan - Furious Angels",
            Plan = plan ?? StemPlan.Default,
            HasWarpGuide = hasWarpGuide,
            Samples = samples,
        };

    private static StemChoice Line(StemPicker picker, string label) =>
        picker.Lines.Single(line => line.Label == label);

    [Fact]
    public void Every_preset_the_separator_offers_is_a_line_whether_or_not_it_was_ever_cut()
    {
        var picker = StemPicker.For(Row(), Catalog);

        Assert.Equal(4, picker.Lines.Count(line => line.Kind is StemKind.Preset));
        Assert.Equal("Maximum vocal capture", Line(picker, "Vocal - Full").Description);
    }

    [Fact]
    public void The_bands_are_the_separators_own_grouping_said_one_at_a_time()
    {
        var labels = StemPicker.For(Row(), Catalog).Bands.Select(band => band.Label);

        Assert.Equal(["Vocal", "Instrumental", "Whole track", "Warp guide"], labels);
    }

    [Fact]
    public void The_four_states_are_on_disk_crossed_with_ticked()
    {
        var picker = StemPicker.For(
            Row(
                new StemPlan { Presets = ["vocal_full", "instrumental_clean"] },
                false,
                Made("vocal_full", "Vocal - Full"),
                Made("instrumental_full", "Instrumental - Full")
            ),
            Catalog
        );

        Assert.Equal(StemState.Kept, Line(picker, "Vocal - Full").State);
        Assert.Equal(StemState.Going, Line(picker, "Instrumental - Full").State);
        Assert.Equal(StemState.Coming, Line(picker, "Instrumental - Clean").State);
        Assert.Equal(StemState.NotMade, Line(picker, "Vocal - Clean").State);

        Assert.Equal(1, picker.Adding);
        Assert.Equal(1, picker.Removing);
        Assert.Equal(1, picker.Keeping);
        Assert.False(picker.IsAdditive);
    }

    [Fact]
    public void Ticking_one_more_preset_removes_nothing()
    {
        var row = Row(StemPlan.Default, true, Made("vocal_full", "Vocal - Full"));

        var picker = StemPicker.For(row, Catalog, row.Plan.Toggling("instrumental_clean"));

        Assert.True(picker.IsAdditive);
        Assert.True(picker.ChangesAnything);
        Assert.Equal(2, picker.Adding);
    }

    [Fact]
    public void Opening_the_picker_on_a_tracks_own_plan_proposes_nothing()
    {
        var picker = StemPicker.For(
            Row(
                StemPlan.Default,
                true,
                Made("vocal_full", "Vocal - Full"),
                Made("instrumental_full", "Instrumental - Full")
            ),
            Catalog
        );

        Assert.False(picker.ChangesAnything);
        Assert.Equal(2, picker.Keeping);
    }

    [Fact]
    public void An_instrumental_release_is_the_vocal_unticked_and_the_source_ticked()
    {
        // The replacement for the has-vocals select, said in the picker's own vocabulary and
        // needing no mode of its own (ADR 0018).
        var picker = StemPicker.For(Row(StemPlan.InstrumentalRelease), Catalog);

        Assert.All(
            picker.Lines.Where(line => line.Kind is StemKind.Preset),
            line => Assert.False(line.IsWanted)
        );

        Assert.True(picker.Lines.Single(line => line.Kind is StemKind.Source).IsWanted);
    }

    [Fact]
    public void The_source_is_a_line_of_its_own_named_after_the_track()
    {
        var source = StemPicker
            .For(Row(), Catalog)
            .Lines.Single(line => line.Kind is StemKind.Source);

        Assert.Equal("Rob Dougan - Furious Angels (Source)", source.Label);
        Assert.False(source.IsPinned);
    }

    [Fact]
    public void The_guide_is_pinned_rather_than_ticked_and_is_never_counted_as_a_file()
    {
        var picker = StemPicker.For(Row(StemPlan.Default, hasWarpGuide: true), Catalog);
        var guide = picker.Lines.Single(line => line.Kind is StemKind.WarpGuide);

        Assert.True(guide.IsPinned);
        Assert.True(guide.IsWanted);

        // Two presets are coming; the guide is not one of them, because it is not kept.
        Assert.Equal(2, picker.Adding);
    }

    [Fact]
    public void A_guide_is_shown_as_coming_only_when_something_else_is()
    {
        var nothingWanted = StemPicker.For(Row(new StemPlan { Presets = [] }), Catalog);

        Assert.False(nothingWanted.Lines.Single(l => l.Kind is StemKind.WarpGuide).IsWanted);

        var somethingWanted = StemPicker.For(Row(), Catalog);

        Assert.True(somethingWanted.Lines.Single(l => l.Kind is StemKind.WarpGuide).IsWanted);
    }

    [Fact]
    public void A_preset_the_separator_has_dropped_is_shown_and_cannot_be_unticked()
    {
        var picker = StemPicker.For(
            Row(
                StemPlan.Default,
                false,
                new TrackSample { Label = "Vocal - Balanced", Kind = StemKind.Unrecognised }
            ),
            Catalog
        );

        var orphan = Line(picker, "Vocal - Balanced");

        Assert.True(orphan.IsPinned);
        Assert.Equal(StemState.Kept, orphan.State);

        // It survives every plan, so nothing here can propose deleting it.
        Assert.Equal(0, picker.Removing);
    }

    [Fact]
    public void A_file_in_the_library_locks_the_whole_picker()
    {
        var picker = StemPicker.For(
            Row(StemPlan.Default, false, Made("vocal_full", "Vocal - Full", inLibrary: true)),
            Catalog
        );

        Assert.True(picker.IsLocked);
    }

    [Fact]
    public void With_no_catalog_the_only_thing_offered_is_the_source()
    {
        // The separator could not be asked, so nothing can say what the presets are called. The
        // panel still draws, and the re-cut refuses separately.
        var picker = StemPicker.For(Row(), PresetCatalog.Empty);

        Assert.DoesNotContain(picker.Lines, line => line.Kind is StemKind.Preset);
        Assert.Equal(["Whole track", "Warp guide"], picker.Bands.Select(band => band.Label));
    }

    [Fact]
    public void A_line_cut_from_more_than_one_upload_carries_both()
    {
        var picker = StemPicker.For(
            Row(
                StemPlan.Default,
                false,
                Made("vocal_full", "Vocal - Full") with
                {
                    SourceVideoId = "aaaaaaaaaaa",
                },
                Made("vocal_full", "Vocal - Full") with
                {
                    SourceVideoId = "bbbbbbbbbbb",
                }
            ),
            Catalog
        );

        Assert.Equal(["aaaaaaaaaaa", "bbbbbbbbbbb"], Line(picker, "Vocal - Full").Uploads);
    }
}

/// <summary>Ticking one line, which is the only edit the picker makes.</summary>
public class StemPlanTogglingTests
{
    [Fact]
    public void Ticking_a_preset_adds_it_and_ticking_it_again_takes_it_away()
    {
        var added = StemPlan.Default.Toggling("instrumental_clean");

        Assert.Equal(["vocal_full", "instrumental_full", "instrumental_clean"], added.Presets);
        Assert.True(added.Toggling("instrumental_clean").AsksTheSameAs(StemPlan.Default));
    }

    [Fact]
    public void It_matches_the_way_a_file_on_disk_is_matched()
    {
        // The join onto provenance is case-insensitive, so ticking must be too, or a plan could
        // hold two spellings of one preset and ask for it twice.
        Assert.DoesNotContain("vocal_full", StemPlan.Default.Toggling("VOCAL_FULL").Presets);
    }

    [Fact]
    public void Ticking_the_source_is_the_only_way_to_reach_an_instrumental_release()
    {
        var plan = StemPlan
            .Default.Toggling("vocal_full")
            .Toggling("instrumental_full")
            .TogglingSource();

        Assert.True(plan.AsksTheSameAs(StemPlan.InstrumentalRelease));
    }
}
