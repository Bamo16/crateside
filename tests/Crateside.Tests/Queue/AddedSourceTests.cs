using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// An added source and a separation source are the same kind of file, which is what makes
/// telling them apart on disk the part worth testing (ADR 0019).
/// </summary>
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

    /// <summary>
    /// The row invites renaming in Ableton, and renaming used to part the file from the plan and
    /// fetch the upload a second time.
    /// </summary>
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
        // Both are Source-kind files. Reading "keep source" as a statement about all of them
        // would delete a file the user attached on purpose and never asked to lose.
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
        // The trap the label would have set: matched on the label, this reads as an added
        // source the plan no longer names, and unticking anything would delete it.
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
        // So it must not enter the separation list. The copy is the download stage's — it used
        // to be the separation runner's, because `--keep-source` was a flag on `separate`, which
        // meant a file copy queued behind the GPU and stopped when separation was paused.
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
