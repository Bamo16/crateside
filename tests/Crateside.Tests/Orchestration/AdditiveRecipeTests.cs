using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// What a cut asks for once the track already has files. The point of the picker: adding a
/// preset should cost the passes it adds and leave everything else alone.
/// </summary>
public class AdditiveRecipeTests
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
                Path = $@"P:\RYAN\Ableton\Sample Staging\x ({preset}).flac",
                Root = @"P:\RYAN\Ableton\Sample Staging",
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

        // One pass rather than three, and the two staged files are untouched — which is also
        // what stops the handoff meeting a filename it already has.
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
        // Deleting the guide is how warping ends, so its absence is the ordinary state of a
        // track that has been worked. Adding a preset later means warping the new file too.
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
                    Path = @"P:\RYAN\Ableton\Sample Staging\Chris Rinox - Burst Mode.flac",
                    Root = @"P:\RYAN\Ableton\Sample Staging",
                },
            ],
            Catalog
        );

        var recipe = SeparationRecipe.For(new StemPlan { Presets = [], KeepSource = true }, kept);

        // Nothing to separate and nothing to copy, so there is nothing to make at all — not even
        // a guide, which is only cut alongside something to warp.
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
    public void Two_recipes_asking_for_the_same_files_are_the_same_recipe()
    {
        // They are built from different arrays, a moment apart. Comparing them by reference made
        // a narrowed recipe unequal to an identical one, which is a lie about a value type.
        Assert.Equal(
            SeparationRecipe.For(StemPlan.Default, StemInventory.Empty),
            SeparationRecipe.For(StemPlan.Default, StemInventory.Empty)
        );
    }
}
