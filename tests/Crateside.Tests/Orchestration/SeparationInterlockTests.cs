using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class SeparationInterlockTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";
    private static readonly DateTimeOffset Launched = new(2026, 8, 13, 22, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "jobs.jsonl"
    );

    private readonly FakeProcessProbe _probe = new();

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    // A fresh pair each time, so every test goes through the file the way a restart does.
    private SeparationInterlock Interlock() => new(new JobStore(_path), _probe);

    [Fact]
    public void Nothing_is_live_before_anything_has_run()
    {
        Assert.False(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void A_claimed_separation_is_live()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        Assert.True(Interlock().TryGetLive(out var job));
        Assert.Equal(VideoId, job.VideoId);
    }

    [Fact]
    public void An_orphan_survives_a_restart_and_is_adopted_rather_than_relaunched()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        // crateside restarts. The child kept running, because on Windows it is not killed
        // with its parent -- so the new instance must find it, not start a second one.
        var reconciliation = Interlock().Reconcile();

        Assert.Equal(VideoId, Assert.Single(reconciliation.Adopted).VideoId);
        Assert.Empty(reconciliation.Abandoned);
        Assert.True(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void An_adoption_is_recorded_on_the_job_and_keeps_the_process()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        Interlock().Reconcile();

        Assert.True(new JobStore(_path).TryGet(VideoId, JobStage.Separation, out var job));
        Assert.NotNull(job.AdoptedAt);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(4242, job.ProcessId);
        Assert.Equal(Launched, job.ProcessStartedAt);
        Assert.True(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void An_adopted_run_that_is_gone_by_the_next_restart_is_abandoned()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);
        _probe.Running[4242] = Launched;
        Interlock().Reconcile();

        _probe.Running.Remove(4242);
        var abandoned = Assert.Single(Interlock().Reconcile().Abandoned);

        Assert.Null(abandoned.AdoptedAt);
        Assert.Equal(JobStatus.Failed, abandoned.Status);
    }

    [Fact]
    public void A_claimed_separation_is_not_adopted()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);

        Assert.True(new JobStore(_path).TryGet(VideoId, JobStage.Separation, out var job));
        Assert.Null(job.AdoptedAt);
    }

    [Fact]
    public void A_process_that_died_without_recording_a_result_is_abandoned_and_strikes()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);

        // The probe knows of no such process: the machine rebooted mid-batch.
        var reconciliation = Interlock().Reconcile();

        var abandoned = Assert.Single(reconciliation.Abandoned);
        Assert.Equal(JobStatus.Failed, abandoned.Status);
        Assert.Equal(1, abandoned.Strikes);
        Assert.Contains("4242", abandoned.Error);
        Assert.False(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void A_recycled_pid_is_not_mistaken_for_our_separation()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);

        // Same id, different process: Windows handed 4242 to something else after the reboot.
        _probe.Running[4242] = Launched.AddHours(9);

        Assert.False(Interlock().TryGetLive(out _));
        Assert.Single(Interlock().Reconcile().Abandoned);
    }

    [Fact]
    public void A_live_pid_with_no_recorded_start_time_is_treated_as_ours()
    {
        // Written by an older crateside that did not record start times. The two mistakes are
        // not symmetrical -- a wrong adopt costs a delay, a wrong launch thrashes the GPU.
        new JobStore(_path).Record(
            VideoId,
            JobStage.Separation,
            JobStatus.Running,
            processId: 4242
        );
        _probe.Running[4242] = Launched;

        Assert.True(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void Releasing_frees_the_gpu()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        Interlock().Release(VideoId);
        _probe.Running.Remove(4242);

        Assert.False(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void A_reported_failure_strikes_but_a_finished_run_does_not()
    {
        var interlock = Interlock();

        interlock.Claim(VideoId, processId: 4242, Launched);
        Assert.Equal(1, interlock.Release(VideoId, error: "cuda out of memory").Strikes);

        interlock.Claim(VideoId, processId: 4243, Launched);
        Assert.Equal(JobStatus.Succeeded, interlock.Release(VideoId).Status);
    }

    [Fact]
    public void Reconciling_a_clean_startup_finds_nothing()
    {
        Interlock().Claim(VideoId, processId: 4242, Launched);
        Interlock().Release(VideoId);

        Assert.False(Interlock().Reconcile().FoundAnything);
    }
}
