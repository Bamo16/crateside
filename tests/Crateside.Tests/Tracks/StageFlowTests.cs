using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

public class StageFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly TrackRow Row = new() { VideoId = "v", Title = "Song" };

    private static Func<JobStage, TrackJob?> Succeeded(params JobStage[] stages) =>
        stage =>
            stages.Contains(stage)
                ? new TrackJob
                {
                    Id = $"v:{stage}",
                    VideoId = "v",
                    Stage = stage,
                    Status = JobStatus.Succeeded,
                    UpdatedAt = Start.AddMinutes((int)stage),
                    Strikes = 0,
                }
                : null;

    [Fact]
    public void A_first_run_that_has_judged_shows_those_stages_done_and_the_rest_not_yet()
    {
        var flow = StageFlow.For(
            Row,
            Succeeded(JobStage.Download, JobStage.Evidence, JobStage.Verdict),
            []
        );

        Assert.Equal(1, flow.Pass);
        Assert.Equal("First run", flow.Reason);
        Assert.Equal(
            ["Download", "Detect", "Judge", "Separate", "Stage & tag", "Warp"],
            flow.Steps.Select(step => step.Label)
        );
        Assert.Equal(
            [FlowState.Done, FlowState.Done, FlowState.Done],
            flow.Steps.Take(3).Select(step => step.State)
        );
        Assert.Equal(Start.AddMinutes((int)JobStage.Verdict), flow.Steps[2].At);
        Assert.Equal(FlowState.NotYet, flow.Steps[4].State);
    }

    [Fact]
    public void Stage_and_tag_is_done_only_once_both_of_its_stages_are()
    {
        var flow = StageFlow.For(Row, Succeeded(JobStage.Handoff), []);

        Assert.Equal(FlowState.NotYet, flow.Steps[4].State);
    }

    [Fact]
    public void A_recut_opens_the_next_pass_and_skips_the_stages_before_separation()
    {
        var flow = StageFlow.For(
            Row,
            Succeeded(JobStage.Download, JobStage.Evidence, JobStage.Verdict),
            [
                new UserAction
                {
                    At = Start,
                    VideoId = "v",
                    Kind = UserActionKind.Recut,
                    PassFrom = JobStage.Separation,
                },
            ]
        );

        Assert.Equal(2, flow.Pass);
        Assert.Equal("Recut", flow.Reason);
        Assert.All(flow.Steps.Take(3), step => Assert.Equal(FlowState.Skipped, step.State));
    }

    [Fact]
    public void The_running_stage_shows_as_running_since_it_started()
    {
        var running = StageFlow.For(
            Row with
            {
                RunningStage = JobStage.Separation,
                RunningSince = Start,
            },
            Succeeded(),
            []
        );

        Assert.Equal(FlowState.Running, running.Steps[3].State);
        Assert.Equal(Start, running.Steps[3].At);
    }

    [Fact]
    public void A_stage_that_ran_out_of_strikes_shows_where_it_stopped()
    {
        var flow = StageFlow.For(
            Row with
            {
                GaveUpAt = JobStage.Handoff,
                GaveUpStrikes = 3,
            },
            Succeeded(JobStage.Download),
            []
        );

        Assert.Equal(FlowState.GaveUp, flow.Steps[4].State);
        Assert.Equal("gave up ×3", flow.Steps[4].Note);
    }
}
