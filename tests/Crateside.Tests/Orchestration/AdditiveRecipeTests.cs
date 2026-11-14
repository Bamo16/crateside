using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Orchestration;

/// <summary>What a cut asks for once the track already has files.</summary>
public sealed class AdditiveRecipeTests
{
    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"},
          {"id":"instrumental_clean","displayName":"Instrumental - Clean","category":"Instrumentals"}
        ]
        """
    );

    private static StemInventory Inventory(params string[] presets) =>
        StemInventory.Of(
            presets.Select(preset => new StemOnDisk
            {
                Path = $@"D:\Music\Sample Staging\x ({preset}).flac",
                Root = @"D:\Music\Sample Staging",
                Preset = preset,
            }),
            Catalog
        );

    [Fact]
    public void A_preset_already_on_disk_is_kept_and_never_cut_again()
    {
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = ["vocal_full", "instrumental_full", "instrumental_clean"] },
            Inventory("Vocal - Full", "Instrumental - Full", "Drums - htdemucs_ft")
        );

        Assert.Equal(["instrumental_clean"], recipe.Presets);
        Assert.Equal(1, recipe.Passes);
    }

    [Fact]
    public void A_guide_already_on_disk_is_not_cut_again_either()
    {
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = ["instrumental_clean"] },
            Inventory("Vocal - Full", "Drums - htdemucs_ft")
        );

        Assert.False(recipe.ExtractDrums);
        Assert.Equal(1, recipe.Passes);
    }

    [Fact]
    public void A_track_whose_guide_has_been_used_up_gets_another_with_its_new_stems()
    {
        // A worked track has no guide, and a new preset still has to be warped.
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = ["vocal_full", "instrumental_clean"] },
            Inventory("Vocal - Full")
        );

        Assert.Equal(["instrumental_clean"], recipe.Presets);
        Assert.True(recipe.ExtractDrums);
    }

    [Fact]
    public void A_source_already_kept_is_not_copied_again()
    {
        var kept = StemInventory.Of(
            [
                new StemOnDisk
                {
                    Path = @"D:\Music\Library\Chris Rinox - Burst Mode.flac",
                    Root = @"D:\Music\Library",
                },
            ],
            Catalog,
            @"D:\Music\Library"
        );

        var recipe = SeparationRecipe.For(new StemPlan { Presets = [], KeepSource = true }, kept);

        Assert.False(recipe.KeepSource);
        Assert.Empty(recipe.Presets);
        Assert.False(recipe.ExtractDrums);
        Assert.Equal(0, recipe.ExpectedOutputs);
    }

    [Fact]
    public void A_track_with_nothing_on_disk_asks_for_the_whole_plan()
    {
        var recipe = SeparationRecipe.For(StemPlan.Default, StemInventory.Empty);

        Assert.Equal(SeparationRecipe.Standard, recipe);
    }

    [Fact]
    public void A_plan_owing_only_its_source_makes_no_passes_at_all()
    {
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = [], KeepSource = true },
            Inventory("Drums - htdemucs_ft")
        );

        Assert.Equal(0, recipe.Passes);
        Assert.True(recipe.KeepSource);
        Assert.False(recipe.ExtractDrums);
    }

    [Fact]
    public void Re_downloading_a_cut_tracks_own_source_asks_for_no_guide_and_no_gpu()
    {
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = ["vocal_full", "instrumental_full"], KeepSource = true },
            // No guide: the track has been worked.
            Inventory("Vocal - Full", "Instrumental - Full")
        );

        Assert.False(recipe.ExtractDrums);
        Assert.True(recipe.KeepSource);
        Assert.Empty(recipe.Presets);

        Assert.Equal(0, recipe.Passes);
    }

    [Fact]
    public void An_instrumental_release_still_gets_its_guide_on_a_first_cut()
    {
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = [], KeepSource = true },
            StemInventory.Empty
        );

        Assert.True(recipe.ExtractDrums);
        Assert.True(recipe.KeepSource);
        Assert.Equal(1, recipe.Passes);
    }

    [Fact]
    public void A_source_arriving_beside_a_guide_that_is_still_there_asks_for_no_second_one()
    {
        var recipe = SeparationRecipe.For(
            new StemPlan { Presets = ["vocal_full"], KeepSource = true },
            Inventory("Vocal - Full", "Drums - htdemucs_ft")
        );

        Assert.False(recipe.ExtractDrums);
        Assert.Equal(0, recipe.Passes);
    }

    [Fact]
    public void Two_recipes_asking_for_the_same_files_are_the_same_recipe()
    {
        // Built from different arrays, so this compares values, not references.
        Assert.Equal(
            SeparationRecipe.For(StemPlan.Default, StemInventory.Empty),
            SeparationRecipe.For(StemPlan.Default, StemInventory.Empty)
        );
    }
}
