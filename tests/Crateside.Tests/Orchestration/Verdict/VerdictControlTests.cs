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
}
