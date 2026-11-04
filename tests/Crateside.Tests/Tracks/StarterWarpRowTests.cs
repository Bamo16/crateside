using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Tracks;

/// <summary>A starter warp is crateside's guess at the tempo, so its stem still waits to be warped (#77).</summary>
public sealed class StarterWarpRowTests : IDisposable
{
    private const string Upload = "AhtkGlO7O8c";
    private const string StagingRoot = @"D:\Music\Sample Staging";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public StarterWarpRowTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_stem_holding_a_starter_warp_reads_as_unwarped_and_a_borrowed_one_as_warped()
    {
        var samples = Row(path =>
            path.Contains("Vocal", StringComparison.Ordinal)
                ? new WrittenWarp(null, 0, default)
                : new WrittenWarp("Song (Instrumental - Clean).flac", 0, default)
        )
            .Samples.ToDictionary(sample => sample.Label);

        Assert.False(samples["Vocal - Full"].HasDefaultClip);
        Assert.True(samples["Instrumental - Full"].HasDefaultClip);
        Assert.Equal(
            "Song (Instrumental - Clean).flac",
            samples["Instrumental - Full"].WarpBorrowedFrom
        );
    }

    [Fact]
    public void A_clip_with_Warp_off_reads_as_unwarped_and_the_users_own_warp_as_warped()
    {
        var samples = Row(path =>
            path.Contains("Vocal", StringComparison.Ordinal)
                ? new WrittenWarp(null, 0, default, IsUnwarped: true)
                : null
        )
            .Samples.ToDictionary(sample => sample.Label);

        Assert.False(samples["Vocal - Full"].HasDefaultClip);
        Assert.True(samples["Instrumental - Full"].HasDefaultClip);
    }

    [Fact]
    public void A_track_whose_stems_hold_only_starter_warps_is_not_ready_to_move() =>
        Assert.NotEqual(TrackState.ReadyToMove, Row(_ => new WrittenWarp(null, 0, default)).State);

    private TrackRow Row(Func<string, WrittenWarp?> writtenWarp) =>
        TracksView
            .Compose(
                WorkListsView.Compose(
                    [],
                    new HashSet<string>(),
                    new HashSet<string>(),
                    new HashSet<string>(),
                    new Dictionary<string, TrackInstruction>()
                ),
                TestLedger.From(Path.Combine(_root, "runs.jsonl")),
                [
                    new StagedSong
                    {
                        SourceTrackKey = Upload,
                        DisplayName = "Song",
                        Stems = [Saved("Vocal - Full"), Saved("Instrumental - Full")],
                    },
                ],
                new Dictionary<string, IReadOnlyList<StemOnDisk>>(),
                @"D:\Music\Library",
                new JobStore(Path.Combine(_root, "jobs.jsonl")),
                new Dictionary<string, TrackInstruction>(),
                readings: new SampleReadings { WrittenWarp = writtenWarp }
            )
            .Rows.Single();

    private static StagedFile Saved(string preset) =>
        new()
        {
            Path = Path.Combine(StagingRoot, $"Song ({preset}).flac"),
            FileName = $"Song ({preset}).flac",
            Preset = preset,
            AbletonKey = "B major",
            HasDefaultClip = true,
        };
}
