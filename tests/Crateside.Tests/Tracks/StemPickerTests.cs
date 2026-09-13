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

    /// <summary>
    /// The window between confirming a cut and its files landing: the plan is recorded, the
    /// track is back in the line for the GPU, and nothing new is on disk yet.
    /// </summary>
    private static TrackRow Cutting(StemPlan plan, params TrackSample[] samples) =>
        Row(plan, hasWarpGuide: true, samples) with
        {
            GpuPosition = 1,
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

        Assert.Equal(["Vocal", "Instrumental", "Sources", "Warp guide"], labels);
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
    public void A_preset_this_workflow_never_wants_is_not_put_in_front_of_you()
    {
        var offered = PresetCatalogReader.Parse(
            """
            [
              {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
              {"id":"vocal_rvc","displayName":"Vocal - RVC","category":"Vocals"},
              {"id":"instrumental_low_resource","displayName":"Instrumental - Low Resource",
               "category":"Instrumentals"}
            ]
            """
        );

        var labels = StemPicker.For(Row(), offered).Lines.Select(line => line.Label);

        Assert.DoesNotContain("Vocal - RVC", labels);
        Assert.DoesNotContain("Instrumental - Low Resource", labels);
        Assert.Contains("Vocal - Full", labels);
    }

    [Fact]
    public void One_a_track_already_has_is_shown_anyway_so_the_file_is_not_hidden()
    {
        // Hiding the line would leave a file on disk with nothing on screen to unpick it — and
        // taking these out of the catalog instead would make it read as a preset the separator
        // no longer offers, which is a different and untrue thing.
        var offered = PresetCatalogReader.Parse(
            """[{"id":"vocal_rvc","displayName":"Vocal - RVC","category":"Vocals"}]"""
        );

        var picker = StemPicker.For(
            Row(new StemPlan { Presets = ["vocal_rvc"] }, false, Made("vocal_rvc", "Vocal - RVC")),
            offered
        );

        Assert.Equal(StemState.Kept, Line(picker, "Vocal - RVC").State);
        Assert.False(Line(picker, "Vocal - RVC").IsPinned);
    }

    [Fact]
    public void A_cut_already_in_flight_is_not_offered_a_second_time()
    {
        // What the user saw: confirming a kept source put the track back in line, and until the
        // copy landed the source line still read as wanted-and-not-on-disk — so the panel offered
        // to cut the very file it was waiting for.
        var picker = StemPicker.For(
            Cutting(
                new StemPlan { Presets = ["vocal_full"], KeepSource = true },
                Made("vocal_full", "Vocal - Full")
            ),
            Catalog
        );

        Assert.Equal(PickerLock.CutInFlight, picker.Lock);

        // The line is still honestly coming — the lock is what says who is making it.
        Assert.Equal(
            StemState.Coming,
            picker.Lines.Single(line => line.Kind is StemKind.Source).State
        );
    }

    [Fact]
    public void A_track_waiting_its_first_turn_on_the_GPU_is_still_free_to_edit()
    {
        // Nothing has been made, so there is nothing to lose and no cut to collide with: the
        // recipe is read when the separator picks the track up, not when the plan was recorded.
        var queued = Row() with
        {
            GpuPosition = 1,
        };

        Assert.False(queued.IsCutInFlight);
        Assert.Equal(PickerLock.None, StemPicker.For(queued, Catalog).Lock);
    }

    [Fact]
    public void A_file_in_the_library_locks_its_own_line_and_nothing_else()
    {
        // crateside does not delete from Mashup Samples, so that one line cannot be unticked.
        // Ticking anything else only ever writes to Sample Staging, so it stays open — which is
        // what lets a finished track gain a preset without moving files back by hand (ADR 0019).
        var picker = StemPicker.For(
            Row(StemPlan.Default, false, Made("vocal_full", "Vocal - Full", inLibrary: true)),
            Catalog
        );

        Assert.False(picker.IsLocked);
        Assert.True(Line(picker, "Vocal - Full").IsPinned);
        Assert.False(Line(picker, "Instrumental - Full").IsPinned);
    }

    [Fact]
    public void With_no_catalog_the_only_thing_offered_is_the_source()
    {
        // The separator could not be asked, so nothing can say what the presets are called. The
        // panel still draws, and the re-cut refuses separately.
        var picker = StemPicker.For(Row(), PresetCatalog.Empty);

        Assert.DoesNotContain(picker.Lines, line => line.Kind is StemKind.Preset);
        Assert.Equal(["Sources", "Warp guide"], picker.Bands.Select(band => band.Label));
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
