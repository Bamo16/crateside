using System.Diagnostics;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class DrainControlTests
{
    [Fact]
    public void The_safe_default_is_paused()
    {
        // Not a change of behaviour — a configured deployment still turns draining on. This is
        // about what happens when configuration does not load, which on 2026-08-16 meant a
        // separation started a second after launch. The fallback must not be the one that
        // consumes the GPU.
        Assert.True(new DrainControl().IsPaused);
        Assert.True(new DrainOptions().StartPaused);
    }

    [Fact]
    public async Task Resuming_wakes_the_worker_instead_of_waiting_out_the_delay()
    {
        var control = new DrainControl(startPaused: true);
        var clock = Stopwatch.StartNew();

        // The real worker passes its two-minute idle delay here. If resume did not signal, this
        // would block for all of it — which is exactly what read as a dead button.
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
        // The signal is level, not edge: two resumes still mean "go". Banking a permit would
        // make the *following* idle wait return instantly for no reason.
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
}
