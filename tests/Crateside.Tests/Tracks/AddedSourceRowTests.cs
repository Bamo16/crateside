using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// How a track wearing an attached source draws, which is where three faults met at once.
/// </summary>
public class AddedSourceRowTests : IDisposable
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
        AbletonFolderInfo? libraryTags = null
    )
    {
        var instruction = new TrackInstruction { VideoId = Track, Plan = Plan };
        var view = QueueView.Compose(
            [new QueueItem { VideoId = Track, Title = "Some Track" }],
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

    /// <summary>
    /// An attached source's audio is a finding about that one file and never parks the track
    /// (ADR 0019).
    /// </summary>
    /// <remarks>
    /// The row used to fall back to any member's acquisition when the primary's had gone, and the
    /// janitor discards the primary's with its working set — so every finished track that gained a
    /// source wore a shortfall banner belonging to the source.
    /// </remarks>
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

    /// <remarks>
    /// A swap supersedes the upload it replaces; an attached source is kept beside the stems, so
    /// calling it an old upload says the one thing that is not true of it.
    /// </remarks>
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

    /// <summary>
    /// A key Ableton holds on a library file reaches the row.
    /// </summary>
    /// <remarks>
    /// The staging board carries this and reads only <c>Sample Staging</c>, so a sample showed a
    /// key until it was finished with and then showed none — while Ableton's browser went on
    /// showing one.
    /// </remarks>
    [Fact]
    public void A_library_samples_key_is_read_from_abletons_index()
    {
        var tags = new AbletonFolderInfo(
            FinishedRoot,
            [AbletonFileTags.FromKeywords("Some Track (Vocal - Full).flac", ["Key|F", "Key|Minor"])]
        );

        var sample = Row(
            [Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")],
            libraryTags: tags
        )
            .Samples.Single();

        Assert.Equal("F minor", sample.AbletonKey);
        Assert.True(sample.IsInLibrary);
    }

    /// <summary>
    /// A track whose files are gone and whose entry is gone can be dropped.
    /// </summary>
    /// <remarks>
    /// Before the intake outlived the playlist entry, deleting a track's files was how it went
    /// away. Now the record holds the row, and a judged track with no files reads as one never
    /// cut — which puts it back in the line for the GPU. Deleting two parked stem sets did
    /// exactly that on 2026-09-16.
    /// </remarks>
    [Fact]
    public void A_track_with_no_entry_and_no_files_can_be_forgotten()
    {
        Assert.True(Row([]).CanForget);
    }

    [Fact]
    public void A_track_that_still_has_files_keeps_its_row_so_forgetting_it_is_not_offered()
    {
        Assert.False(
            Row([Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")]).CanForget
        );
    }

    [Fact]
    public void A_library_sample_Ableton_has_no_key_for_reads_as_untagged()
    {
        var sample = Row(
            [Library("Some Track (Vocal - Full).flac", Track, "Vocal - Full")],
            libraryTags: AbletonFolderInfo.Empty(FinishedRoot)
        )
            .Samples.Single();

        Assert.Null(sample.AbletonKey);
    }
}
