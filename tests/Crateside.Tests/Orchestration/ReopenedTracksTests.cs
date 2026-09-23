using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// What counts as work a track is still owed, which is what decides whether it can be reached at
/// all once the playlist has consumed it.
/// </summary>
public class ReopenedTracksTests
{
    private const string VideoId = "_DiMKyZwaA0";

    private static KeyLedger Ledger =>
        KeyLedger.Read(Path.Combine(Path.GetTempPath(), "crateside-tests", "no-such-ledger.jsonl"));

    /// <summary>
    /// Moe Shop - HARDCODED: a separation killed mid-run, on a track the playlist had already let
    /// go of. Without this it is in no work list, the re-cut offers nothing because the plan
    /// already asks for the missing file, and Retry is hidden until the strikes run out.
    /// </summary>
    [Fact]
    public void A_failed_separation_with_strikes_left_is_still_owed()
    {
        TrackJob[] jobs = [Job(JobStage.Separation, JobStatus.Failed, strikes: 1)];

        Assert.Equal([VideoId], ReopenedTracks.From(jobs, Ledger));
        Assert.Equal([VideoId], ReopenedTracks.AwaitingCut(jobs, Ledger));
    }

    [Fact]
    public void A_separation_that_has_given_up_is_not_owed()
    {
        // It would otherwise sit in the line for a GPU at a position it never advances from (#32).
        // Retry is the way out by then, and it is on screen because the strikes ran out.
        TrackJob[] jobs =
        [
            Job(JobStage.Separation, JobStatus.Failed, SeparationSelector.DefaultMaxStrikes),
        ];

        Assert.Empty(ReopenedTracks.From(jobs, Ledger));
    }

    [Fact]
    public void A_failed_handoff_owes_work_without_owing_the_gpu_anything()
    {
        TrackJob[] jobs = [Job(JobStage.Handoff, JobStatus.Failed, strikes: 1)];

        Assert.Equal([VideoId], ReopenedTracks.From(jobs, Ledger));
        Assert.Empty(ReopenedTracks.AwaitingCut(jobs, Ledger));
    }

    [Fact]
    public void A_stage_that_succeeded_owes_nothing()
    {
        TrackJob[] jobs = [Job(JobStage.Separation, JobStatus.Succeeded, strikes: 0)];

        Assert.Empty(ReopenedTracks.From(jobs, Ledger));
    }

    [Theory]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    public void The_states_that_already_owed_work_still_do(JobStatus status)
    {
        TrackJob[] jobs = [Job(JobStage.Separation, status, strikes: 0)];

        Assert.Equal([VideoId], ReopenedTracks.From(jobs, Ledger));
    }

    private static TrackJob Job(JobStage stage, JobStatus status, int strikes) =>
        new()
        {
            Id = $"{VideoId}:{stage}",
            VideoId = VideoId,
            Stage = stage,
            Status = status,
            UpdatedAt = DateTimeOffset.Parse("2026-09-23T19:19:15+00:00"),
            Strikes = strikes,
        };
}
