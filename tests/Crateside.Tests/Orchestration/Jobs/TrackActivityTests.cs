using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class TrackActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static JobUpdate Job(
        int minute,
        JobStage stage,
        JobStatus status,
        string? error = null
    ) =>
        new()
        {
            Id = $"v:{stage}",
            VideoId = "v",
            Stage = stage,
            Status = status,
            Timestamp = Start.AddMinutes(minute),
            Error = error,
        };

    private static UserAction Action(int minute, UserActionKind kind, JobStage? passFrom = null) =>
        new()
        {
            At = Start.AddMinutes(minute),
            VideoId = "v",
            Kind = kind,
            PassFrom = passFrom,
        };

    [Fact]
    public void Stages_deletions_and_actions_merge_in_time_order()
    {
        var entries = TrackActivity.Build(
            [
                Job(0, JobStage.Download, JobStatus.Running),
                Job(2, JobStage.Download, JobStatus.Succeeded),
            ],
            [
                new Deletion
                {
                    At = Start.AddMinutes(3),
                    FileName = "Song.flac",
                    Reason = "dropped from the plan by a re-cut",
                },
            ],
            [Action(1, UserActionKind.GenreSettled)]
        );

        Assert.Equal(
            ["Download", TrackActivity.You, "Download", TrackActivity.Crateside],
            entries.Select(entry => entry.Source)
        );
        Assert.Equal("recycled Song.flac: dropped from the plan by a re-cut", entries[^1].Message);
    }

    [Fact]
    public void An_action_that_restarts_a_stage_opens_the_next_pass_and_comes_before_its_stage()
    {
        var entries = TrackActivity.Build(
            [
                Job(0, JobStage.Separation, JobStatus.Succeeded),
                Job(5, JobStage.Separation, JobStatus.Running),
            ],
            [],
            [Action(5, UserActionKind.Recut, JobStage.Separation), Action(4, UserActionKind.Bumped)]
        );

        Assert.Equal([1, 1, 2, 2], entries.Select(entry => entry.Pass));
        Assert.Equal(TrackActivity.You, entries[2].Source);
    }

    [Fact]
    public void A_failure_is_a_problem_and_carries_its_error()
    {
        var entry = Assert.Single(
            TrackActivity.Build(
                [Job(0, JobStage.Handoff, JobStatus.Failed, "already in staging")],
                [],
                []
            )
        );

        Assert.True(entry.IsProblem);
        Assert.Equal("failed: already in staging", entry.Message);
    }

    [Fact]
    public void Back_to_back_identical_lines_fold_into_one_with_a_count_and_the_last_time()
    {
        var entries = TrackActivity.Build(
            [
                Job(0, JobStage.Handoff, JobStatus.Failed, "already in staging"),
                Job(1, JobStage.Handoff, JobStatus.Failed, "already in staging"),
                Job(2, JobStage.Handoff, JobStatus.Failed, "already in staging"),
                Job(3, JobStage.Handoff, JobStatus.Succeeded),
            ],
            [],
            []
        );

        Assert.Equal(2, entries.Count);
        Assert.Equal(3, entries[0].Count);
        Assert.Equal(Start.AddMinutes(2), entries[0].At);
    }

    [Fact]
    public void Every_track_in_this_machines_job_log_reads_as_a_timeline_that_never_loses_a_line()
    {
        var jobs = new JobStore();

        Assert.SkipUnless(File.Exists(jobs.Path), "Needs this machine's jobs.jsonl.");

        var videoIds = jobs.All().Select(job => job.VideoId).Distinct().ToList();

        Assert.NotEmpty(videoIds);

        foreach (var videoId in videoIds)
        {
            var history = jobs.History(videoId);
            var entries = TrackActivity.Build(history, [], []);

            Assert.Equal(history.Count, entries.Sum(entry => entry.Count));
            Assert.True(entries.Zip(entries.Skip(1)).All(pair => pair.First.At <= pair.Second.At));
        }
    }
}
