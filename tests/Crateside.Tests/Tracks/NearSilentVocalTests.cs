using Crateside.Core.Ledger;
using Crateside.Core.Loudness;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Tracks;

public sealed class NearSilentVocalTests : IDisposable
{
    private const string Track = "aaaaaaaaaaa";

    private static readonly string FinishedRoot = Path.Combine(
        Path.GetTempPath(),
        "Mashup Samples"
    );

    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"}
        ]
        """
    );

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public NearSilentVocalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(-70.0, -15.0, true)]
    [InlineData(-36.7, -12.7, true)]
    [InlineData(-28.6, -11.5, true)]
    [InlineData(-27.4, -10.2, true)]
    [InlineData(-14.0, -12.0, false)]
    [InlineData(-30.0, -14.0, false)]
    [InlineData(-30.1, -14.0, true)]
    public void A_vocal_more_than_16_dB_under_the_instrumental_is_flagged(
        double vocal,
        double instrumental,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            NearSilentVocal.IsFlagged([("vocal_full", vocal), ("instrumental_full", instrumental)])
        );

    [Fact]
    public void Nothing_is_flagged_without_both_stems_measured()
    {
        Assert.False(NearSilentVocal.IsFlagged([("vocal_full", -60.0)]));
        Assert.False(
            NearSilentVocal.IsFlagged([("vocal_full", -60.0), ("instrumental_full", null)])
        );
        Assert.False(
            NearSilentVocal.IsFlagged([("vocal_full", null), ("instrumental_full", -10.0)])
        );
        Assert.False(NearSilentVocal.IsFlagged([(null, -60.0), ("instrumental_full", -10.0)]));
    }

    [Fact]
    public void A_plan_without_a_vocal_has_nothing_to_offer()
    {
        Assert.True(NearSilentVocal.AsksForVocal(StemPlan.Default));
        Assert.False(NearSilentVocal.AsksForVocal(StemPlan.InstrumentalRelease));
    }

    private TrackRow RowWith(Func<string, double?> lufsOf, StemPlan? plan = null)
    {
        var instruction = new TrackInstruction { TrackKey = Track, Plan = plan };
        var view = WorkListsView.Compose(
            [new WorkItem { TrackKey = Track, Title = "Some Track" }],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction> { [Track] = instruction }
        );

        var path = Path.Combine(_root, "runs.jsonl");

        File.WriteAllText(path, string.Empty);

        StemOnDisk Stem(string preset) =>
            new()
            {
                Path = Path.Combine(FinishedRoot, $"Some Track ({preset}).flac"),
                Root = FinishedRoot,
                Preset = preset,
                SourceTrackKey = Track,
            };

        return Assert.Single(
            TracksView
                .Compose(
                    view,
                    TestLedger.From(path),
                    [],
                    new Dictionary<string, IReadOnlyList<StemOnDisk>>
                    {
                        [Track] = [Stem("Vocal - Full"), Stem("Instrumental - Full")],
                    },
                    FinishedRoot,
                    new JobStore(Path.Combine(_root, "jobs.jsonl")),
                    new Dictionary<string, TrackInstruction> { [Track] = instruction },
                    readings: new SampleReadings { Presets = Catalog, LufsOf = lufsOf }
                )
                .Rows
        );
    }

    private static double? Quiet(string path) => path.Contains("Vocal") ? -50.0 : -14.0;

    [Fact]
    public void A_row_whose_vocal_stem_is_near_silent_says_so()
    {
        Assert.True(RowWith(Quiet).HasNearSilentVocal);
    }

    [Fact]
    public void A_row_with_an_ordinary_vocal_is_not_flagged()
    {
        Assert.False(RowWith(_ => -14.0).HasNearSilentVocal);
    }

    [Fact]
    public void A_row_already_switched_to_the_instrumental_recipe_is_not_flagged_again()
    {
        Assert.False(RowWith(Quiet, StemPlan.InstrumentalRelease).HasNearSilentVocal);
    }

    [Fact]
    public void A_row_with_no_loudness_kept_is_not_flagged()
    {
        Assert.False(RowWith(_ => null).HasNearSilentVocal);
    }
}
