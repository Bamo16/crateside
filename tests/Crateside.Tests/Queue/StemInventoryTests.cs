using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

public class StemInventoryTests
{
    private const string Staging = @"P:\RYAN\Ableton\Sample Staging";

    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"},
          {"id":"instrumental_clean","displayName":"Instrumental - Clean","category":"Instrumentals"}
        ]
        """
    );

    private static StemOnDisk File(string name, string? preset) =>
        new()
        {
            Path = Path.Combine(Staging, name),
            Root = Staging,
            Preset = preset,
        };

    [Fact]
    public void A_stems_provenance_names_the_preset_that_made_it()
    {
        var inventory = StemInventory.Of(
            [
                File("Burial - Archangel (Vocal - Full).flac", "Vocal - Full"),
                File("Burial - Archangel (Instrumental - Full).flac", "Instrumental - Full"),
            ],
            Catalog
        );

        Assert.Equal(["instrumental_full", "vocal_full"], inventory.Presets.Order());
        Assert.False(inventory.HasSource);
        Assert.Empty(inventory.Unrecognised);
    }

    [Fact]
    public void A_stem_written_before_provenance_carried_a_preset_is_read_off_its_name()
    {
        // Old StemForge wrote the models it used and no preset at all.
        var inventory = StemInventory.Of(
            [File("Awich - Queendom (Vocal - Full).flac", preset: null)],
            Catalog
        );

        Assert.Equal(["vocal_full"], inventory.Presets);
        Assert.False(inventory.HasSource);
    }

    [Theory]
    // A kept source with nothing parenthesised at all.
    [InlineData("Chris Rinox - Burst Mode.flac")]
    // Kept sources whose own title ends in brackets — the trap the name-reading fallback sets.
    [InlineData("Ayatsuno Yuni - SUPADOPA (Official Instrumental).flac")]
    [InlineData("Kreayshawn - Gucci Gucci (Acapella).flac")]
    [InlineData("Chrono Trigger Soundtrack - Schala's Theme (Instrumental).flac")]
    public void A_download_kept_whole_is_a_source_whatever_its_title_ends_in(string name)
    {
        var inventory = StemInventory.Of([File(name, preset: null)], Catalog);

        Assert.True(inventory.HasSource);
        Assert.Empty(inventory.Presets);
        Assert.Empty(inventory.Unrecognised);
    }

    [Fact]
    public void The_drums_are_the_warp_guide_and_never_a_preset_the_plan_asks_for()
    {
        var inventory = StemInventory.Of(
            [File("Atura - Think About You (Drums).flac", "Drums - htdemucs_ft")],
            Catalog
        );

        Assert.True(inventory.HasWarpGuide);
        Assert.Empty(inventory.Presets);
        Assert.Empty(inventory.Unrecognised);
    }

    [Fact]
    public void A_preset_the_separator_no_longer_offers_is_neither_a_source_nor_forgotten()
    {
        var inventory = StemInventory.Of(
            [File("Kubbi - Pathfinder (Vocal - Balanced).flac", "Vocal - Balanced")],
            Catalog
        );

        Assert.False(inventory.HasSource);
        Assert.Empty(inventory.Presets);
        Assert.Equal("Vocal - Balanced", Assert.Single(inventory.Unrecognised).DisplayName);
    }

    [Fact]
    public void Outstanding_is_what_the_plan_asks_for_and_the_disk_has_not_got()
    {
        var inventory = StemInventory.Of([File("x (Vocal - Full).flac", "Vocal - Full")], Catalog);

        var outstanding = inventory.Shortfall(
            new StemPlan { Presets = ["vocal_full", "instrumental_clean"] }
        );

        Assert.Equal(["instrumental_clean"], outstanding.Presets);
        Assert.False(outstanding.KeepSource);
    }

    [Fact]
    public void A_plan_the_disk_already_satisfies_is_outstanding_nothing()
    {
        var inventory = StemInventory.Of(
            [File("x (Vocal - Full).flac", "Vocal - Full"), File("x.flac", preset: null)],
            Catalog
        );

        var outstanding = inventory.Shortfall(
            new StemPlan { Presets = ["vocal_full"], KeepSource = true }
        );

        Assert.True(outstanding.IsEmpty);
    }

    [Fact]
    public void A_track_with_nothing_on_disk_owes_the_whole_plan()
    {
        Assert.True(
            StemInventory.Empty.Shortfall(StemPlan.Default).AsksTheSameAs(StemPlan.Default)
        );
    }

    [Fact]
    public void Superseded_is_what_is_on_disk_and_no_longer_wanted()
    {
        var inventory = StemInventory.Of(
            [
                File("x (Vocal - Full).flac", "Vocal - Full"),
                File("x (Instrumental - Full).flac", "Instrumental - Full"),
                File("x.flac", preset: null),
                File("x (Drums).flac", "Drums - htdemucs_ft"),
            ],
            Catalog
        );

        var going = inventory.Superseded(new StemPlan { Presets = ["instrumental_full"] });

        Assert.Equal(
            ["x (Vocal - Full).flac", "x.flac"],
            going.Select(file => Path.GetFileName(file.File.Path))
        );
    }

    [Fact]
    public void The_warp_guide_is_not_the_plans_to_drop()
    {
        var inventory = StemInventory.Of([File("x (Drums).flac", "Drums - htdemucs_ft")], Catalog);

        Assert.Empty(inventory.Superseded(new StemPlan { Presets = [] }));
    }

    [Fact]
    public void An_unrecognised_stem_is_not_swept_away_by_a_plan_that_cannot_name_it()
    {
        var inventory = StemInventory.Of(
            [File("x (Vocal - Balanced).flac", "Vocal - Balanced")],
            Catalog
        );

        Assert.Empty(inventory.Superseded(StemPlan.Default));
    }

    [Fact]
    public void With_no_catalog_every_stem_reads_as_unrecognised_rather_than_as_a_source()
    {
        // The separator could not be asked.
        var inventory = StemInventory.Of(
            [File("x (Vocal - Full).flac", "Vocal - Full"), File("x.flac", preset: null)],
            PresetCatalog.Empty
        );

        Assert.Empty(inventory.Presets);
        Assert.Single(inventory.Unrecognised);
        Assert.True(inventory.HasSource);
    }
}
