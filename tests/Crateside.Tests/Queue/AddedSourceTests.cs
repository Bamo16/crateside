using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>Telling an added source from a separation source on disk (ADR 0019).</summary>
public class AddedSourceTests
{
    private const string Staging = @"P:\RYAN\Ableton\Sample Staging";
    private const string Track = "aaaaaaaaaaa";
    private const string Added = "bbbbbbbbbbb";

    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """[{"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"}]"""
    );

    private static StemOnDisk File(string name, string videoId, string? preset = null) =>
        new()
        {
            Path = Path.Combine(Staging, name),
            Root = Staging,
            Preset = preset,
            SourceVideoId = videoId,
        };

    private static readonly AddedSource Official = new()
    {
        VideoId = Added,
        Label = "Official Instrumental",
    };

    [Fact]
    public void An_added_source_counts_as_fetched_once_its_file_carries_the_label()
    {
        var inventory = StemInventory.Of(
            [File("SUPADOPA (Official Instrumental).flac", Added)],
            Catalog
        );

        Assert.True(inventory.Has(Official));
    }

    [Fact]
    public void An_added_source_stays_fetched_after_the_user_renames_it()
    {
        var inventory = StemInventory.Of([File("Some Other Name (Acapella).flac", Added)], Catalog);

        Assert.True(inventory.Has(Official));
    }

    /// <remarks>
    /// Pre-1.0 files carry no provenance at all, so the label stays the fallback for them.
    /// </remarks>
    [Fact]
    public void A_file_with_no_provenance_is_still_matched_on_its_label()
    {
        var inventory = StemInventory.Of(
            [File("SUPADOPA (Official Instrumental).flac", videoId: null!)],
            Catalog
        );

        Assert.True(inventory.Has(Official));
    }

    [Fact]
    public void A_track_without_it_yet_still_owes_it()
    {
        var inventory = StemInventory.Of([File("SUPADOPA.flac", Track)], Catalog);

        Assert.False(inventory.Has(Official));
    }

    [Fact]
    public void Dropping_an_added_source_removes_its_file_and_leaves_the_separation_source()
    {
        var inventory = StemInventory.Of(
            [File("SUPADOPA.flac", Track), File("SUPADOPA (Official Instrumental).flac", Added)],
            Catalog
        );

        var stored = new StemPlan
        {
            Presets = [],
            KeepSource = true,
            AddedSources = [Official],
        };

        var going = inventory.Superseded(stored.Dropping(Official.Label), stored);

        Assert.Equal(
            "SUPADOPA (Official Instrumental).flac",
            Path.GetFileName(Assert.Single(going).File.Path)
        );
    }

    [Fact]
    public void Unticking_the_separation_source_leaves_the_added_ones_alone()
    {
        var inventory = StemInventory.Of(
            [File("SUPADOPA.flac", Track), File("SUPADOPA (Official Instrumental).flac", Added)],
            Catalog
        );

        var stored = new StemPlan
        {
            Presets = [],
            KeepSource = true,
            AddedSources = [Official],
        };

        var going = inventory.Superseded(stored.TogglingSource(), stored);

        Assert.Equal("SUPADOPA.flac", Path.GetFileName(Assert.Single(going).File.Path));
    }

    [Fact]
    public void A_separation_source_whose_own_title_ends_in_brackets_is_not_an_added_one()
    {
        var inventory = StemInventory.Of(
            [File("Schala's Theme (Instrumental).flac", Track)],
            Catalog
        );

        var stored = new StemPlan { Presets = [], KeepSource = true };

        Assert.Empty(inventory.Superseded(stored, stored));
    }

    [Fact]
    public void A_plan_that_keeps_its_source_gives_the_separator_nothing_to_do()
    {
        // So it must not enter the separation list: the download stage copies it.
        var plan = new StemPlan { Presets = [], KeepSource = true };
        var guideOnly = StemInventory.Of(
            [File("x (Drums).flac", Track, "Drums - htdemucs_ft")],
            Catalog
        );

        Assert.True(guideOnly.Shortfall(plan).KeepSource);
        Assert.Equal(0, SeparationRecipe.For(plan, guideOnly).Passes);
        Assert.Equal(0, SeparationRecipe.For(plan, guideOnly).ExpectedOutputs);
    }

    [Fact]
    public void Adding_and_dropping_are_the_same_plan_either_side()
    {
        var plan = StemPlan.Default.Adding(Official);

        Assert.Equal([Official], plan.AddedSources);
        Assert.Empty(plan.Dropping("official instrumental").AddedSources);
    }
}
