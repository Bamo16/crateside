using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class VerdictControlTests
{
    [Fact]
    public void Form_runs_count_as_waiting_until_their_scope_ends()
    {
        var control = new VerdictControl();
        var changes = 0;
        control.Changed += () => changes++;

        var first = control.WaitingFormRun();
        var second = control.WaitingFormRun();

        Assert.Equal(2, control.FormRunsWaiting);

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, control.FormRunsWaiting);

        second.Dispose();

        Assert.Equal(0, control.FormRunsWaiting);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void A_hold_is_kept_and_announced_until_cleared()
    {
        var control = new VerdictControl();
        var changes = 0;
        control.Changed += () => changes++;
        var hold = new JudgeHold("rate limited", null);

        control.SetHold(hold);
        control.SetHold(hold);

        Assert.Equal(hold, control.Hold);
        Assert.Equal(1, changes);

        control.SetHold(null);

        Assert.Null(control.Hold);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void An_activity_records_when_it_began_and_forgets_it_when_it_ends()
    {
        var control = new VerdictControl();

        control.Report("Judging Song");

        Assert.NotNull(control.ActivitySince);

        control.Report(null);

        Assert.Null(control.ActivitySince);
    }

    [Fact]
    public void A_judging_session_names_its_track_until_the_stage_goes_idle()
    {
        var control = new VerdictControl();

        control.ReportJudging("abc123", "Judging abc123");

        Assert.Equal("abc123", control.JudgingVideoId);
        Assert.Equal("Judging abc123", control.Activity);

        control.ReportIdle();

        Assert.Null(control.JudgingVideoId);
        Assert.Null(control.Activity);
    }
}
