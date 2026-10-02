using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Plans;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ableton;

namespace Crateside.Tests.Tracks;

/// <summary>How a track wearing an attached source draws.</summary>
public sealed class AddedSourceRowTests : IDisposable
{
    private const string Track = "aaaaaaaaaaa";
    private const string Attached = "bbbbbbbbbbb";
    private const string FinishedRoot = @"P:\RYAN\Ableton\Mashup Samples";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public AddedSourceRowTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static readonly AddedSource Source = new() { VideoId = Attached, Label = "A Cappella" };

    private static readonly StemPlan Plan = new()
    {
        Presets = ["vocal_full"],
        AddedSources = [Source],
    };

    /// <summary>A download that stops: no premium ladder, and nobody has accepted it.</summary>
    private static readonly Acquisition Shortfall = new()
    {
        PremiumStatus = PremiumStatus.NoPremiumLadder,
        IsPremium = false,
        IsAccepted = false,
    };

    private JobStore Jobs() => new(Path.Combine(_root, "jobs.jsonl"));

    private KeyLedger Ledger()
    {
        var path = Path.Combine(_root, "runs.jsonl");

        File.WriteAllText(path, string.Empty);

        return KeyLedger.Read(path);
    }

    private static StemOnDisk Library(string name, string videoId, string? preset = null) =>
        new()
        {
            Path = Path.Combine(FinishedRoot, name),
            Root = FinishedRoot,
            Preset = preset,
            SourceVideoId = videoId,
        };

    private TrackRow Row(
        IReadOnlyList<StemOnDisk> files,
        IReadOnlyDictionary<string, Acquisition>? acquisitions = null,
        FolderInfo? libraryTags = null
    )
    {
        var instruction = new TrackInstruction { VideoId = Track, Plan = Plan };
        var view = WorkListsView.Compose(
            [new WorkItem { VideoId = Track, Title = "Some Track" }],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction> { [Track] = instruction }
        );

        return Assert.Single(
            TracksView
                .Compose(
                    view,
                    Ledger(),
                    [],
                    new Dictionary<string, IReadOnlyList<StemOnDisk>> { [Track] = files },
                    FinishedRoot,
                    Jobs(),
                    new Dictionary<string, TrackInstruction> { [Track] = instruction },
                    resolvedNames: null,
                    acquisitions,
                    presets: null,
                    libraryTags
                )
                .Rows
        );
    }

    [Fact]
    public void An_attached_source_that_is_not_premium_says_so_long_after_it_landed()
    {
        // Read from the file itself, not the acquisition record, which the janitor discards.
        var row = Row([
            Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full"),
            Library("Some Track (A Cappella).flac", Attached) with
            {
                IsPremium = false,
            },
        ]);

        var line = new StemChoice
        {
            Label = "A Cappella",
            Kind = StemKind.Source,
            IsWanted = true,
            Files = [.. row.Samples.Where(sample => sample.SourceVideoId == Attached)],
        };

        Assert.True(line.IsNotPremium);
    }

    [Fact]
    public void A_poor_attached_source_does_not_park_the_track()
    {
        var row = Row(
            [Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")],
            new Dictionary<string, Acquisition> { [Attached] = Shortfall }
        );

        Assert.Null(row.ParkedFor);
    }

    [Fact]
    public void The_tracks_own_poor_download_still_parks_it()
    {
        var row = Row(
            [Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")],
            new Dictionary<string, Acquisition> { [Track] = Shortfall }
        );

        Assert.Equal(ParkReason.PremiumShortfall, row.ParkedFor);
    }

    [Fact]
    public void An_attached_upload_is_not_an_old_upload()
    {
        var row = Row([
            Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full"),
            Library("Some Track (A Cappella).flac", Attached),
        ]);

        Assert.True(row.HasSeveralUploads);
        Assert.Single(row.Plan.AddedSources);
        Assert.True(row.IsAddedSource(Attached));
        Assert.False(row.IsAddedSource(Track));
    }

    [Fact]
    public void A_library_samples_key_is_read_from_abletons_index()
    {
        var tags = new FolderInfo(
            FinishedRoot,
            [TestTags.Of("Some Track (Vocal - Full).flac", ["Key|F", "Key|Minor"])]
        );

        var sample = Row(
            [Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")],
            libraryTags: tags
        )
            .Samples.Single();

        Assert.Equal("F minor", sample.AbletonKey);
        Assert.True(sample.IsInLibrary);
    }

    [Fact]
    public void A_track_with_no_entry_and_no_files_can_be_forgotten()
    {
        Assert.True(Row([]).CanForget);
    }

    [Fact]
    public void A_track_with_a_file_in_the_library_keeps_its_row_so_removing_it_is_not_offered()
    {
        Assert.False(
            Row([Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")]).CanForget
        );
    }

    [Fact]
    public void A_track_with_files_only_in_staging_can_be_removed()
    {
        var row = new TrackRow
        {
            VideoId = Track,
            Title = "Rob Dougan - Clubbed to Death",
            Samples = [new TrackSample { Label = "source" }],
        };

        Assert.True(row.CanForget);
    }

    [Fact]
    public void A_track_mid_stage_cannot_be_removed()
    {
        // The handoff picks stems by provenance, so a running cut would stage them again after.
        var row = new TrackRow
        {
            VideoId = Track,
            Title = "Rob Dougan - Clubbed to Death",
            RunningStage = JobStage.Separation,
        };

        Assert.False(row.CanForget);
    }

    [Fact]
    public void A_library_sample_Ableton_has_no_key_for_reads_as_untagged()
    {
        var sample = Row(
            [Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")],
            libraryTags: FolderInfo.Empty(FinishedRoot)
        )
            .Samples.Single();

        Assert.Null(sample.AbletonKey);
    }
}
