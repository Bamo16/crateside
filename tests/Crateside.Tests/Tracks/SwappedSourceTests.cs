using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Tracks;

/// <summary>One song cut from two uploads (ADR 0014).</summary>
public sealed class SwappedSourceTests : IDisposable
{
    private const string Original = "AhtkGlO7O8c";
    private const string Replacement = "39Z_YiOmHeA";
    private const string Song = "ludacris, pharrell - southern hospitality";
    private const string FinishedRoot = @"P:\RYAN\Ableton\Mashup Samples";
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public SwappedSourceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_ledger_resolves_both_uploads_to_one_song()
    {
        var ledger = Ledger();

        Assert.Equal(Song, ledger.SongFor(Original));
        Assert.Equal(Song, ledger.SongFor(Replacement));
    }

    [Fact]
    public void A_video_id_the_ledger_has_never_seen_has_no_song() =>
        Assert.Null(Ledger().SongFor("zzzzzzzzzzz"));

    [Fact]
    public void Both_uploads_share_one_row()
    {
        var rows = Compose();

        Assert.Single(rows);
    }

    /// <summary>The replacement's third file is its warp guide, which is not a sample.</summary>
    [Fact]
    public void The_row_carries_every_sample_from_both_uploads()
    {
        var samples = Compose().Single().Samples;

        Assert.Equal(4, samples.Count);
        Assert.Equal(2, samples.Count(s => s.SourceVideoId == Replacement));
        Assert.Equal(2, samples.Count(s => s.SourceVideoId == Original));
    }

    [Fact]
    public void The_replacements_warp_guide_is_still_outstanding() =>
        Assert.True(Compose().Single().HasWarpGuide);

    [Fact]
    public void Every_sample_says_which_upload_it_came_from() =>
        Assert.All(Compose().Single().Samples, sample => Assert.NotNull(sample.SourceVideoId));

    [Fact]
    public void The_old_uploads_samples_are_the_ones_already_in_the_library()
    {
        var samples = Compose().Single().Samples;

        Assert.All(
            samples.Where(s => s.SourceVideoId == Original),
            sample => Assert.True(sample.IsInLibrary)
        );
        Assert.All(
            samples.Where(s => s.SourceVideoId == Replacement),
            sample => Assert.False(sample.IsInLibrary)
        );
    }

    [Fact]
    public void A_stage_completed_under_the_replacement_settles_it_for_the_song()
    {
        TrackJob[] jobs =
        [
            Job(Original, JobStage.Separation, JobStatus.Succeeded, "00:37:49"),
            Job(Original, JobStage.Handoff, JobStatus.Cancelled, "00:22:02"),
            Job(Original, JobStage.Tagging, JobStatus.Cancelled, "00:22:02"),
            Job(Replacement, JobStage.Handoff, JobStatus.Succeeded, "00:39:43"),
            Job(Replacement, JobStage.Tagging, JobStatus.Succeeded, "02:43:36"),
        ];

        Assert.Empty(ReopenedTracks.From(jobs, Ledger()));
    }

    [Fact]
    public void A_stage_nothing_has_finished_still_owes_work()
    {
        TrackJob[] jobs =
        [
            Job(Original, JobStage.Separation, JobStatus.Succeeded, "00:37:49"),
            Job(Original, JobStage.Handoff, JobStatus.Cancelled, "00:22:02"),
        ];

        Assert.Equal([Original], ReopenedTracks.From(jobs, Ledger()));
    }

    [Fact]
    public void A_cancellation_later_than_the_success_reopens_the_song_again()
    {
        TrackJob[] jobs =
        [
            Job(Replacement, JobStage.Handoff, JobStatus.Succeeded, "00:39:43"),
            Job(Original, JobStage.Handoff, JobStatus.Cancelled, "01:00:00"),
        ];

        Assert.Equal([Original], ReopenedTracks.From(jobs, Ledger()));
    }

    [Fact]
    public void A_track_the_ledger_does_not_know_is_still_judged_by_its_own_video_id()
    {
        TrackJob[] jobs = [Job("zzzzzzzzzzz", JobStage.Handoff, JobStatus.Cancelled, "00:22:02")];

        Assert.Equal(["zzzzzzzzzzz"], ReopenedTracks.From(jobs, Ledger()));
    }

    private IReadOnlyList<TrackRow> Compose() =>
        TracksView
            .Compose(
                WorkListsView.Compose(
                    [],
                    new HashSet<string>(),
                    new HashSet<string>(),
                    new HashSet<string>(),
                    new Dictionary<string, TrackInstruction>()
                ),
                Ledger(),
                [Staged()],
                OnDisk(),
                FinishedRoot,
                new JobStore(Path.Combine(_root, "jobs.jsonl")),
                new Dictionary<string, TrackInstruction>()
            )
            .Rows;

    private static StagedSong Staged() =>
        new()
        {
            SourceVideoId = Replacement,
            DisplayName = "Ludacris, Pharrell - Southern Hospitality",
            Stems =
            [
                StagedStem("Vocal - Full"),
                StagedStem("Instrumental - Full"),
                StagedStem("Drums"),
            ],
        };

    private static StagedFile StagedStem(string preset) =>
        new()
        {
            Path = Path.Combine(StagingRoot, $"Southern Hospitality ({preset}).flac"),
            FileName = $"Southern Hospitality ({preset}).flac",
            Preset = preset,
            AbletonKey = "B major",
        };

    private static Dictionary<string, IReadOnlyList<StemOnDisk>> OnDisk() =>
        new(StringComparer.Ordinal)
        {
            [Original] =
            [
                Found("Vocal - Full", FinishedRoot),
                Found("Instrumental - Full", FinishedRoot),
            ],
            [Replacement] =
            [
                Found("Vocal - Full", StagingRoot),
                Found("Instrumental - Full", StagingRoot),
                Found("Drums", StagingRoot),
            ],
        };

    private static StemOnDisk Found(string preset, string root) =>
        new()
        {
            Path = Path.Combine(root, $"Southern Hospitality ({preset}).flac"),
            Root = root,
            Preset = preset,
        };

    private static TrackJob Job(string videoId, JobStage stage, JobStatus status, string at) =>
        new()
        {
            Id = $"{videoId}:{stage}",
            VideoId = videoId,
            Stage = stage,
            Status = status,
            UpdatedAt = DateTimeOffset.Parse($"2026-09-10T{at}+00:00"),
            Strikes = 0,
        };

    private KeyLedger Ledger()
    {
        var path = Path.Combine(_root, "runs.jsonl");

        File.WriteAllLines(
            path,
            [
                Line("2026-08-06T20:35:49+00:00", Original, restates: null),
                Line("2026-09-09T22:47:26+00:00", Replacement, "2026-08-06T20:35:49+00:00"),
            ]
        );

        return TestLedger.From(path);
    }

    private static string Line(string timestamp, string videoId, string? restates) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp,
                song = Song,
                artist = "Ludacris, Pharrell",
                title = "Southern Hospitality",
                source_url = $"https://music.youtube.com/watch?v={videoId}",
                verdict = new { key = "B major", confidence = "MEDIUM" },
                bpm = new { value = 95.0 },
                restates,
            }
        );
}
