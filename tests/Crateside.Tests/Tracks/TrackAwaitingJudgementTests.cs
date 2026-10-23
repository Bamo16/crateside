using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>The judging cell's "waiting" count, which should not move as tracks download and detect.</summary>
public sealed class TrackAwaitingJudgementTests
{
    private static readonly LedgerRun Judged = new()
    {
        Timestamp = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero),
        Song = "darude - sandstorm (radio edit)",
    };

    private static TrackRow Row() => new() { VideoId = "XjvkxXblpz8", Title = "Sandstorm" };

    [Theory]
    [InlineData(null)]
    [InlineData(JobStage.Download)]
    [InlineData(JobStage.Evidence)]
    public void An_unjudged_track_counts_whether_queued_downloading_or_detecting(
        JobStage? running
    ) => Assert.True((Row() with { RunningStage = running }).IsAwaitingJudgement);

    [Fact]
    public void The_track_being_judged_is_not_waiting() =>
        Assert.False((Row() with { RunningStage = JobStage.Verdict }).IsAwaitingJudgement);

    [Fact]
    public void A_judged_track_is_not_waiting() =>
        Assert.False((Row() with { Verdict = Judged }).IsAwaitingJudgement);

    [Fact]
    public void A_track_held_on_a_question_is_not_waiting() =>
        Assert.False((Row() with { Lookalike = Judged }).IsAwaitingJudgement);

    [Fact]
    public void A_track_that_gave_up_is_not_waiting() =>
        Assert.False(
            (Row() with { GaveUpAt = JobStage.Download, GaveUpStrikes = 5 }).IsAwaitingJudgement
        );

    [Fact]
    public void A_parked_track_is_not_waiting() =>
        Assert.False((Row() with { ParkedFor = ParkReason.PremiumShortfall }).IsAwaitingJudgement);

    [Fact]
    public void The_library_backfill_waits_for_the_weekly_spend_down_not_the_judging_stage()
    {
        var row = Row() with { IsBackfill = true };

        Assert.False(row.IsAwaitingJudgement);
        Assert.Equal("waiting for the weekly spend-down", row.Next);
    }
}
