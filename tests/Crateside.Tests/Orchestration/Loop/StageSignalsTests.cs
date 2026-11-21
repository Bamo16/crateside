using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Loop;

public sealed class StageSignalsTests
{
    private static readonly TimeSpan LongEnoughToBlock = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ShortEnoughToElapse = TimeSpan.FromMilliseconds(50);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Waking_a_stage_ends_its_wait_without_ending_anyone_elses()
    {
        var signals = Signals();

        var download = signals.For(JobStage.Download).WaitForWorkAsync(LongEnoughToBlock, Token);

        signals.Wake(JobStage.Download);

        Assert.True(await download);
        Assert.False(
            await signals.For(JobStage.Handoff).WaitForWorkAsync(ShortEnoughToElapse, Token)
        );
    }

    [Fact]
    public async Task A_pausable_stage_is_woken_through_the_control_its_worker_waits_on()
    {
        // A fresh signal for Verdict would take the wake while the worker waits on VerdictControl.
        var verdict = new VerdictControl(startPaused: false);
        var signals = new StageSignals(new DrainControl(startPaused: false), verdict);

        var waiting = verdict.WaitForWorkAsync(LongEnoughToBlock, Token);

        signals.Wake(JobStage.Verdict);

        Assert.True(await waiting);
    }

    [Fact]
    public async Task A_stage_that_ran_wakes_the_others_and_not_itself()
    {
        // A copy lands in Key Identified, which the handoff reads (#48).
        var signals = Signals();

        var handoff = signals.For(JobStage.Handoff).WaitForWorkAsync(LongEnoughToBlock, Token);

        signals.WakeAllBut(JobStage.Download);

        Assert.True(await handoff);
        Assert.False(
            await signals.For(JobStage.Download).WaitForWorkAsync(ShortEnoughToElapse, Token)
        );
    }

    [Fact]
    public async Task Every_stage_has_a_signal_of_its_own()
    {
        var signals = Signals();

        foreach (var stage in Enum.GetValues<JobStage>())
        {
            signals.Wake(stage);

            Assert.True(
                await signals.For(stage).WaitForWorkAsync(LongEnoughToBlock, Token),
                $"{stage} was not woken"
            );
        }
    }

    private static StageSignals Signals() =>
        new(new DrainControl(startPaused: false), new VerdictControl(startPaused: false));
}
