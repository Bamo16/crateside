using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Plans;
using Crateside.Core.Playback;
using Crateside.Core.Tracks;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Playback;

public sealed class MixStatusTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";

    private static readonly DateTimeOffset Left = new(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
            WorkingFolder = Path.Combine(_root, "working"),
            ImportsFolder = Path.Combine(_root, "imports"),
        };

    private WorkingSet Set => field ??= new WorkingSet(Paths);

    private SourceRetentionStore Retention =>
        field ??= new SourceRetentionStore(Path.Combine(_root, "retention.json"));

    private MixStatus Status =>
        new(
            new PlayableFiles(
                new SourceIndex(Set, Path.Combine(_root, "source-index.json")),
                Paths
            ),
            Set,
            Retention,
            new MixFetches(
                new SourceBackfill(
                    TestLedger.Store(),
                    Set,
                    new DownloadRunner(new ProcessRunner()),
                    TimeProvider.System,
                    Paths
                )
            ),
            Paths
        );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private void HoldMix(DateTimeOffset? leftStaging = null, bool adHoc = false)
    {
        File.WriteAllBytes(Path.Combine(Set.Prepare(TrackKey), "source.flac"), [1]);

        if (leftStaging is { } left)
            Set.RecordLeftStaging(TrackKey, left, adHoc);
    }

    private static TrackRow Row(StemPlan? plan = null, params TrackSample[] samples) =>
        new()
        {
            TrackKey = TrackKey,
            Title = "Artist - Song",
            Plan = plan ?? StemPlan.Default,
            Samples = samples,
        };

    [Fact]
    public void A_mix_in_the_working_set_is_held_until_its_retention_runs_out()
    {
        HoldMix(Left);
        Retention.Remember(new SourceRetention { Days = 3 });

        Assert.Equal(new MixState.Held(Left.AddDays(3), false), Status.Of(Row()));
    }

    [Fact]
    public void A_mix_fetched_again_is_held_at_least_a_day_whatever_the_retention()
    {
        HoldMix(Left, adHoc: true);
        Retention.Remember(new SourceRetention { Days = 0 });

        Assert.Equal(new MixState.Held(Left.AddDays(1), false), Status.Of(Row()));
    }

    [Fact]
    public void A_mix_kept_forever_has_no_date()
    {
        HoldMix(Left);
        Retention.Remember(new SourceRetention { Forever = true });

        Assert.Equal(new MixState.Held(null, true), Status.Of(Row()));
    }

    [Fact]
    public void A_mix_the_library_holds_as_a_sample_is_in_the_library()
    {
        var kept = Path.Combine(Paths.LibraryFolder, "Song (Source).flac");
        Directory.CreateDirectory(Paths.LibraryFolder);
        File.WriteAllBytes(kept, [1]);

        var row = Row(
            null,
            new TrackSample
            {
                Path = kept,
                Label = "source",
                Kind = StemKind.Source,
                IsInLibrary = true,
            }
        );

        Assert.IsType<MixState.InLibrary>(Status.Of(row));
    }

    [Fact]
    public void A_mix_the_plan_keeps_but_the_library_does_not_hold_yet_is_being_kept()
    {
        HoldMix(Left);

        Assert.IsType<MixState.Keeping>(
            Status.Of(Row(StemPlan.Default with { KeepSource = true }))
        );
    }

    [Fact]
    public void A_mix_that_is_gone_can_be_fetched_again_when_it_came_from_an_upload()
    {
        Assert.Equal(new MixState.Gone(true), Status.Of(Row()));
    }

    [Fact]
    public void An_imported_file_that_is_gone_cannot_be_fetched_again()
    {
        var row = Row() with { TrackKey = "file~0123456789abcdef" };

        Assert.Equal(new MixState.Gone(false), Status.Of(row));
    }

    [Fact]
    public void An_imported_mix_is_never_pruned()
    {
        var folder = Path.Combine(Paths.ImportsFolder, TrackKey);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "song.flac"), [1]);

        Assert.IsType<MixState.Imported>(Status.Of(Row()));
    }
}
