using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.Staging;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Plans;

/// <summary>Asks the real separator, and joins its answer against the real library.</summary>
public sealed class PresetCatalogRealDataTests
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string MashupRoot = @"P:\RYAN\Ableton\Mashup Samples";

    private static async Task<PresetCatalog> RealCatalogAsync()
    {
        var catalog = await new PresetCatalogReader().ReadAsync(
            TestContext.Current.CancellationToken
        );

        Assert.SkipWhen(catalog.IsEmpty, "stemforge-cli is not on PATH, or reported no presets.");

        return catalog;
    }

    [Fact]
    public async Task The_separator_reports_a_display_name_for_every_preset_it_offers()
    {
        var catalog = await RealCatalogAsync();

        Assert.All(
            catalog.Presets,
            preset =>
            {
                Assert.NotEmpty(preset.Id);
                Assert.NotEmpty(preset.DisplayName);
                Assert.NotEmpty(preset.Category);
            }
        );

        Assert.Equal("Vocal - Full", catalog.ById("vocal_full")?.DisplayName);
        Assert.Equal("Instrumental - Full", catalog.ById("instrumental_full")?.DisplayName);
    }

    [Fact]
    public async Task Every_preset_the_library_carries_resolves_back_to_an_id()
    {
        Assert.SkipUnless(
            File.Exists(LedgerPath) && Directory.Exists(MashupRoot),
            "Needs the real ledger and stem roots."
        );

        var catalog = await RealCatalogAsync();

        var unresolved = ProcessedTracks
            .Scan(TestLedger.From(LedgerPath), [StagingRoot, MashupRoot])
            .StemsByVideoId.SelectMany(entry => entry.Value)
            .Select(file => file.Preset)
            .Where(preset => preset is not null && !StagedFile.IsWarpGuidePreset(preset))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(preset => !catalog.TryGetId(preset, out _))
            .ToArray();

        Assert.Empty(unresolved);
    }
}
