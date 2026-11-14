using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration;

public sealed class JobHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_track_tagged_again_counts_once_in_the_week_it_was_first_tagged()
    {
        var history = JobHistory.From(
            [
                Update("a", JobStage.Tagging, JobStatus.Succeeded, Now.AddDays(-8)),
                Update("a", JobStage.Tagging, JobStatus.Succeeded, Now),
                Update("b", JobStage.Tagging, JobStatus.Succeeded, Now),
            ],
            Now
        );

        Assert.Equal(
            [new WeekFinished(39, 1, IsCurrent: false), new WeekFinished(40, 1, IsCurrent: true)],
            history.Weeks
        );
    }

    [Fact]
    public void A_week_with_nothing_finished_still_has_its_column()
    {
        var history = JobHistory.From(
            [Update("a", JobStage.Tagging, JobStatus.Succeeded, Now.AddDays(-14))],
            Now
        );

        Assert.Equal([38, 39, 40], history.Weeks.Select(week => week.Week));
        Assert.Equal(0, history.Weeks[^1].Tracks);
    }

    [Fact]
    public void Stages_are_totalled_by_outcome()
    {
        var history = JobHistory.From(
            [
                Update("a", JobStage.Separation, JobStatus.Running, Now.AddMinutes(-9)),
                Update("a", JobStage.Separation, JobStatus.Failed, Now.AddMinutes(-8)),
                Update("a", JobStage.Separation, JobStatus.Succeeded, Now.AddMinutes(-2)),
                Update("a", JobStage.Handoff, JobStatus.Canceled, Now.AddMinutes(-1)),
            ],
            Now
        );

        Assert.Equal(
            new StageTotal(JobStage.Separation, Done: 1, Failed: 1, Canceled: 0),
            history.Stages[0]
        );
        Assert.Equal(1, history.Stages.Single(stage => stage.Stage is JobStage.Handoff).Canceled);
    }

    [Fact]
    public void A_hold_does_not_count_as_a_cancellation_or_a_failure_however_many_retries_it_took()
    {
        var until = Now.AddMinutes(30);
        var history = JobHistory.From(
            [
                Update("a", JobStage.Verdict, JobStatus.Succeeded, Now.AddMinutes(-9)),
                Update("b", JobStage.Verdict, JobStatus.Running, Now.AddMinutes(-8)),
                Update(
                    "b",
                    JobStage.Verdict,
                    JobStatus.Held,
                    Now.AddMinutes(-8),
                    "rate limited",
                    until
                ),
                Update("b", JobStage.Verdict, JobStatus.Running, Now.AddMinutes(-7)),
                Update(
                    "b",
                    JobStage.Verdict,
                    JobStatus.Held,
                    Now.AddMinutes(-7),
                    "rate limited",
                    until
                ),
                Update("b", JobStage.Verdict, JobStatus.Running, Now.AddMinutes(-6)),
                Update("b", JobStage.Verdict, JobStatus.Succeeded, Now.AddMinutes(-5)),
            ],
            Now
        );

        Assert.Equal(
            new StageTotal(JobStage.Verdict, Done: 2, Failed: 0, Canceled: 0),
            history.Stages.Single(stage => stage.Stage is JobStage.Verdict)
        );
    }

    [Fact]
    public void A_hold_an_older_crateside_recorded_as_canceled_is_read_as_a_hold()
    {
        const string Old =
            "the judge\x27s endpoint reported its rate limit; judging tries again shortly";
        var history = JobHistory.From(
            [
                Update("b", JobStage.Verdict, JobStatus.Running, Now.AddMinutes(-4)),
                Update("b", JobStage.Verdict, JobStatus.Canceled, Now.AddMinutes(-4), Old),
                Update("b", JobStage.Verdict, JobStatus.Running, Now.AddMinutes(-3)),
                Update("b", JobStage.Verdict, JobStatus.Canceled, Now.AddMinutes(-3), Old),
            ],
            Now
        );

        Assert.Equal(0, history.Stages.Single(stage => stage.Stage is JobStage.Verdict).Canceled);
    }

    [Fact]
    public void A_real_judging_cancellation_still_shows_and_counts()
    {
        var history = JobHistory.From(
            [
                Update("b", JobStage.Verdict, JobStatus.Running, Now.AddMinutes(-2)),
                Update(
                    "b",
                    JobStage.Verdict,
                    JobStatus.Canceled,
                    Now.AddMinutes(-1),
                    "did not survive a restart"
                ),
            ],
            Now
        );

        Assert.Equal(1, history.Stages.Single(stage => stage.Stage is JobStage.Verdict).Canceled);
    }

    private static JobUpdate Update(
        string trackKey,
        JobStage stage,
        JobStatus status,
        DateTimeOffset at,
        string? error = null,
        DateTimeOffset? heldUntil = null
    ) =>
        new()
        {
            Id = $"{trackKey}:{stage}",
            TrackKey = trackKey,
            Stage = stage,
            Status = status,
            Timestamp = at,
            Error = error,
            HeldUntil = heldUntil,
        };
}
