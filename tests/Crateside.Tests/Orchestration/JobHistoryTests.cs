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
    public void Stages_are_totalled_by_outcome_and_the_latest_come_newest_first()
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
        Assert.Equal("handoff canceled", JobHistory.Describe(history.Latest[0]));
        Assert.Equal("separated", JobHistory.Describe(history.Latest[1]));
    }

    private static JobUpdate Update(
        string videoId,
        JobStage stage,
        JobStatus status,
        DateTimeOffset at
    ) =>
        new()
        {
            Id = $"{videoId}:{stage}",
            VideoId = videoId,
            Stage = stage,
            Status = status,
            Timestamp = at,
        };
}
