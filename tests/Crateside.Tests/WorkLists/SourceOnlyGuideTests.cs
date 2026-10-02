using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.WorkLists;

/// <summary>A track that keeps only its source still gets a drums warp guide once it is staged (#55).</summary>
public sealed class SourceOnlyGuideTests
{
    private const string Track = "aaaaaaaaaaa";
    private const string Staging = @"P:\RYAN\Ableton\Sample Staging";
    private const string Library = @"P:\RYAN\Ableton\Mashup Samples";

    private static readonly StemPlan SourceOnly = new() { Presets = [], KeepSource = true };

    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """[{"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"}]"""
    );

    private static StemInventory Files(string root, params string?[] presets) =>
        StemInventory.Of(
            presets.Select(preset => new StemOnDisk
            {
                Path = $@"{root}\x{(preset is null ? string.Empty : $" ({preset})")}.flac",
                Root = root,
                Preset = preset,
            }),
            Catalog,
            Library
        );

    [Fact]
    public void A_staged_source_with_no_guide_asks_for_the_guide_alone()
    {
        var recipe = SeparationRecipe.For(SourceOnly, Files(Staging, [null]));

        Assert.True(recipe.ExtractDrums);
        Assert.Empty(recipe.Presets);
        Assert.False(recipe.KeepSource);
        Assert.Equal(1, recipe.ExpectedOutputs);
    }

    [Fact]
    public void A_source_already_in_the_library_asks_for_nothing()
    {
        var recipe = SeparationRecipe.For(SourceOnly, Files(Library, [null]));

        Assert.False(recipe.ExtractDrums);
        Assert.Equal(0, recipe.Passes);
    }

    [Fact]
    public void A_staged_source_beside_its_guide_asks_for_nothing()
    {
        var recipe = SeparationRecipe.For(SourceOnly, Files(Staging, null, "Drums - htdemucs_ft"));

        Assert.Equal(0, recipe.Passes);
    }

    [Fact]
    public void A_judged_staged_source_joins_the_separation_list()
    {
        var view = Compose(Files(Staging, [null]), SourceOnly);

        Assert.Equal(Track, Assert.Single(view.AwaitingSeparation).VideoId);
    }

    [Fact]
    public void A_finished_source_only_track_does_not_come_back_for_a_guide()
    {
        Assert.Empty(Compose(Files(Library, [null]), SourceOnly).AwaitingSeparation);
    }

    [Fact]
    public void A_staged_track_missing_a_stem_is_not_re_cut_by_the_same_route()
    {
        var plan = new StemPlan { Presets = ["vocal_full"], KeepSource = true };

        Assert.Empty(Compose(Files(Staging, [null]), plan).AwaitingSeparation);
    }

    private static WorkListsView Compose(StemInventory inventory, StemPlan plan) =>
        WorkListsView.Compose(
            [new WorkItem { VideoId = Track, Title = "Track" }],
            new HashSet<string> { Track },
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>
            {
                [Track] = new() { VideoId = Track, Plan = plan },
            },
            inventories: new Dictionary<string, StemInventory> { [Track] = inventory }
        );
}
