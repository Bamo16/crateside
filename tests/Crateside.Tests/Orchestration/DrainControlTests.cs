using System.Diagnostics;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class DrainControlTests
{
    [Fact]
    public void The_safe_default_is_paused()
    {
        // What happens when configuration does not load.
        Assert.True(new DrainControl().IsPaused);
    }

    [Fact]
    public async Task Resuming_wakes_the_worker_instead_of_waiting_out_the_delay()
    {
        var control = new DrainControl(startPaused: true);
        var clock = Stopwatch.StartNew();

        // The real worker passes its two-minute idle delay here.
        var waiting = control.WaitForWorkAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken
        );

        control.Resume();

        Assert.True(await waiting);
        Assert.False(control.IsPaused);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"waited {clock.Elapsed}");
    }

    [Fact]
    public async Task The_delay_still_elapses_when_nothing_happens()
    {
        var control = new DrainControl(startPaused: false);

        Assert.False(
            await control.WaitForWorkAsync(
                TimeSpan.FromMilliseconds(50),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Resuming_twice_does_not_bank_a_second_wake()
    {
        var control = new DrainControl(startPaused: true);

        control.Resume();
        control.Pause();
        control.Resume();

        Assert.True(
            await control.WaitForWorkAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            )
        );
        Assert.False(
            await control.WaitForWorkAsync(
                TimeSpan.FromMilliseconds(50),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Pausing_does_not_wake_anything()
    {
        var control = new DrainControl(startPaused: false);

        control.Pause();

        Assert.False(
            await control.WaitForWorkAsync(
                TimeSpan.FromMilliseconds(50),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public void Judgment_starts_paused_too()
    {
        Assert.True(new VerdictControl().IsPaused);
    }

    [Fact]
    public void The_two_stages_stop_independently()
    {
        var separation = new DrainControl(startPaused: false);
        var verdict = new VerdictControl(startPaused: false);

        verdict.Pause();

        Assert.True(verdict.IsPaused);
        Assert.False(separation.IsPaused);
    }
}
