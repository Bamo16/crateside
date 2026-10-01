using Crateside.Core.Orchestration.Separation;

namespace Crateside.Tests.Plans;

public sealed class PresetCatalogTests
{
    private const string TwoPresets = """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals",
           "description":"Maximum vocal capture","algorithm":"max_fft","models":["a.ckpt"]},
          {"id":"instrumental_clean","displayName":"Instrumental - Clean",
           "category":"Instrumentals","description":"Cleanest instrumentals",
           "algorithm":"uvr_max_spec","models":["b.ckpt"]}
        ]
        """;

    [Fact]
    public void It_reads_the_separators_own_words_for_each_preset()
    {
        var catalog = PresetCatalogReader.Parse(TwoPresets);

        Assert.Equal(2, catalog.Presets.Count);
        Assert.Equal("Vocal - Full", catalog.ById("vocal_full")?.DisplayName);
        Assert.Equal("Vocals", catalog.ById("vocal_full")?.Category);
        Assert.Equal("Maximum vocal capture", catalog.ById("vocal_full")?.Description);
    }

    [Fact]
    public void A_name_a_file_carries_resolves_to_the_id_a_plan_asks_for()
    {
        var catalog = PresetCatalogReader.Parse(TwoPresets);

        Assert.True(catalog.TryGetId("Instrumental - Clean", out var id));
        Assert.Equal("instrumental_clean", id);
    }

    [Theory]
    // The warp guide, which carries the model name and is not a catalog preset.
    [InlineData("Drums - htdemucs_ft")]
    // A source download, which StemForge tags deliberately without a preset.
    [InlineData(null)]
    [InlineData("Vocal - Balanced")]
    public void A_name_the_separator_no_longer_offers_resolves_to_nothing(string? displayName)
    {
        Assert.False(PresetCatalogReader.Parse(TwoPresets).TryGetId(displayName, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void Both_lookups_ignore_case_the_way_the_filesystem_does()
    {
        var catalog = PresetCatalogReader.Parse(TwoPresets);

        Assert.NotNull(catalog.ById("VOCAL_FULL"));
        Assert.True(catalog.TryGetId("vocal - full", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("[{\"id\":\"x\"}]")] // missing required members
    public void Anything_unreadable_is_an_empty_catalog_rather_than_a_throw(string stdout)
    {
        Assert.True(PresetCatalogReader.Parse(stdout).IsEmpty);
    }

    [Fact]
    public void Log_lines_before_the_array_are_not_ours_to_interpret()
    {
        var catalog = PresetCatalogReader.Parse($"warning: something\n{TwoPresets}");

        Assert.Equal(2, catalog.Presets.Count);
    }
}
