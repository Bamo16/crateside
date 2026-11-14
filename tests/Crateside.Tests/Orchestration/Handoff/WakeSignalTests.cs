using Crateside.Core.Orchestration.Handoff;

namespace Crateside.Tests.Orchestration.Handoff;

public sealed class WakeSignalTests
{
    [Fact]
    public void A_set_after_arming_completes_the_task_arming_returned()
    {
        var signal = new WakeSignal();
        var waiting = signal.Arm();

        signal.Set();

        Assert.True(waiting.IsCompletedSuccessfully);
    }

    [Fact]
    public void A_set_before_arming_does_not_complete_the_next_wait()
    {
        var signal = new WakeSignal();

        signal.Set();

        Assert.False(signal.Arm().IsCompleted);
    }
}
