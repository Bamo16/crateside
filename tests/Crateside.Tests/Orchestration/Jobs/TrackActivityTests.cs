using Crateside.Core;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class TrackActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static JobUpdate Job(
        int minute,
        JobStage stage,
        JobStatus status,
        string? error = null,
        string? summary = null,
        DateTimeOffset? heldUntil = null
    ) =>
        new()
        {
            Id = $"v:{stage}",
            TrackKey = "v",
            Stage = stage,
            Status = status,
            Timestamp = Start.AddMinutes(minute),
            Error = error,
            Summary = summary,
            HeldUntil = heldUntil,
        };

    private static UserAction Action(int minute, UserActionKind kind, string? detail = null) =>
        new()
        {
            At = Start.AddMinutes(minute),
            TrackKey = "v",
            Kind = kind,
            Detail = detail,
        };

    [Fact]
    public void A_job_is_one_row_under_the_stage_name_the_dashboard_uses()
    {
        var row = Assert.Single(
            TrackActivity.Build(
                [
                    Job(0, JobStage.Evidence, JobStatus.Running),
                    Job(2, JobStage.Evidence, JobStatus.Succeeded, summary: "keydetect B major"),
                ],
                [],
                []
            )
        );

        Assert.Equal("Detection", row.Label);
        Assert.Equal("keydetect B major", row.Text);
        Assert.Equal(TimeSpan.FromMinutes(2), row.Took(Start.AddHours(1)));
    }

    [Theory]
    [InlineData(JobStage.Download, "Download")]
    [InlineData(JobStage.Evidence, "Detection")]
    [InlineData(JobStage.Verdict, "Judging")]
    [InlineData(JobStage.Separation, "Separation")]
    [InlineData(JobStage.Handoff, "Handoff")]
    [InlineData(JobStage.Tagging, "Tagging")]
    public void Every_stage_has_its_dashboard_name(JobStage stage, string name) =>
        Assert.Equal(name, ActivityFeed.StageName(stage));

    [Fact]
    public void Jobs_deletions_and_actions_merge_newest_first()
    {
        var rows = TrackActivity.Build(
            [
                Job(0, JobStage.Download, JobStatus.Running),
                Job(2, JobStage.Download, JobStatus.Succeeded, summary: "downloaded"),
            ],
            [
                new Deletion
                {
                    At = Start.AddMinutes(3),
                    TrackKey = "v",
                    FileName = "Song.flac",
                    Reason = "dropped from the plan by a re-cut",
                },
            ],
            [Action(1, UserActionKind.GenreSettled, "House")]
        );

        Assert.Equal(
            [TrackActivity.Crateside, TrackActivity.You, "Download"],
            rows.Select(row => row.Label)
        );
        Assert.Equal("recycled Song.flac: dropped from the plan by a re-cut", rows[0].Text);
        Assert.Equal("genre settled: House", rows[1].Text);
    }

    [Fact]
    public void An_action_and_the_stage_it_wakes_share_a_moment_with_the_stage_above()
    {
        var rows = TrackActivity.Build(
            [Job(5, JobStage.Separation, JobStatus.Running)],
            [],
            [Action(5, UserActionKind.Recut)]
        );

        Assert.Equal(["Separation", TrackActivity.You], rows.Select(row => row.Label));
    }

    [Fact]
    public void A_failure_is_a_problem_that_reads_as_failed_with_its_error()
    {
        var row = Assert.Single(
            TrackActivity.Build(
                [Job(0, JobStage.Handoff, JobStatus.Failed, "already in staging")],
                [],
                []
            )
        );

        Assert.True(row.IsFailedOrCanceled);
        Assert.Equal("failed: already in staging", row.FullText);
    }

    [Fact]
    public void A_canceled_job_is_a_problem_and_stays_in_the_list()
    {
        var row = Assert.Single(
            TrackActivity.Build(
                [Job(0, JobStage.Separation, JobStatus.Canceled, "stopped by you")],
                [],
                []
            )
        );

        Assert.Equal(ActivityState.Canceled, row.State);
        Assert.True(row.IsFailedOrCanceled);
        Assert.StartsWith("canceled: ", row.FullText);
    }

    [Fact]
    public void A_stage_still_running_is_one_live_row_with_a_duration_so_far()
    {
        var row = Assert.Single(
            TrackActivity.Build([Job(0, JobStage.Verdict, JobStatus.Running)], [], [])
        );

        Assert.True(row.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(7), row.Took(Start.AddMinutes(7)));
    }

    [Fact]
    public void A_finish_written_before_summaries_says_it_finished_rather_than_nothing()
    {
        var row = Assert.Single(
            TrackActivity.Build(
                [
                    Job(0, JobStage.Download, JobStatus.Running),
                    Job(2, JobStage.Download, JobStatus.Succeeded),
                ],
                [],
                []
            )
        );

        Assert.Equal("finished, no details recorded", row.Text);
        Assert.Equal(TimeSpan.FromMinutes(2), row.Took(Start.AddHours(1)));
    }

    [Fact]
    public void A_held_stage_says_why()
    {
        var row = Assert.Single(
            TrackActivity.Build(
                [
                    Job(
                        0,
                        JobStage.Verdict,
                        JobStatus.Held,
                        "the judge's endpoint can't be reached",
                        heldUntil: Start.AddMinutes(1)
                    ),
                ],
                [],
                []
            )
        );

        Assert.Equal(ActivityState.Held, row.State);
        Assert.Equal("held: the judge's endpoint can't be reached", row.Text);
    }

    [Fact]
    public void A_stage_note_is_a_row_under_its_own_name()
    {
        var rows = TrackActivity.Build(
            [Job(0, JobStage.Handoff, JobStatus.Succeeded, summary: "staged 2 stems")],
            [],
            [],
            notes:
            [
                new StageNote
                {
                    At = Start.AddMinutes(2),
                    TrackKey = "v",
                    Stage = "Warp",
                    Summary = "Vocal - Full: Warp off: one tempo misses by 12 ms",
                },
            ]
        );

        Assert.Equal(
            [
                ("Warp", "Vocal - Full: Warp off: one tempo misses by 12 ms"),
                ("Handoff", "staged 2 stems"),
            ],
            rows.Select(row => (row.Label, row.Text))
        );
    }

    [Fact]
    public void Only_the_persons_own_rows_are_theirs_and_an_api_caller_is_not()
    {
        var rows = TrackActivity.Build(
            [],
            [],
            [
                Action(0, UserActionKind.Bumped),
                new UserAction
                {
                    At = Start.AddMinutes(1),
                    TrackKey = "v",
                    Kind = UserActionKind.Rejudged,
                    By = "agent: coordination",
                },
            ]
        );

        Assert.Equal(["agent: coordination", TrackActivity.You], rows.Select(row => row.Label));
        Assert.Equal([false, true], rows.Select(TrackActivity.IsYou));
    }

    [Fact]
    public void Every_row_has_its_own_sequence_so_expanding_one_opens_only_that_one()
    {
        var rows = TrackActivity.Build(
            [Job(0, JobStage.Download, JobStatus.Succeeded, summary: "downloaded")],
            [],
            [Action(1, UserActionKind.Bumped), Action(2, UserActionKind.Bumped)]
        );

        Assert.Equal(rows.Count, rows.Select(row => row.Sequence).Distinct().Count());
    }

    [Fact]
    public void A_copied_line_has_the_time_the_label_the_text_and_how_long_it_took()
    {
        var row = Assert.Single(
            TrackActivity.Build(
                [
                    Job(0, JobStage.Separation, JobStatus.Running),
                    Job(6, JobStage.Separation, JobStatus.Failed, "out of memory"),
                ],
                [],
                []
            )
        );

        Assert.Equal(
            "12:00  Separation  failed: out of memory · 6 min",
            TrackActivity.CopyLine(row, Start.AddHours(1), at => at.ToString("HH:mm"))
        );
    }

    [Fact]
    public void Every_track_in_this_machines_job_log_reads_as_rows_with_text()
    {
        var jobs = new JobStore(new CratesidePaths());

        Assert.SkipUnless(File.Exists(jobs.Path), "Needs this machine's jobs.jsonl.");

        var trackKeys = jobs.All().Select(job => job.TrackKey).Distinct().ToList();

        Assert.NotEmpty(trackKeys);

        foreach (var trackKey in trackKeys)
        {
            var rows = TrackActivity.Build(jobs.History(trackKey), [], []);

            Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Text)));
            Assert.True(
                rows.Zip(rows.Skip(1)).All(pair => pair.First.StartedAt >= pair.Second.StartedAt)
            );
        }
    }
}
