using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;

namespace Crateside.Tests.Orchestration.Separation;

public sealed class SeparationInterlockTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";
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

    private static readonly SeparationPromise ThreeStems = new(3, StemDestination.UnresolvedKey);

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            StemsKeyIdentified = Path.Combine(Path.GetDirectoryName(_path)!, "Key Identified"),
            StemsUnresolvedKey = Path.Combine(Path.GetDirectoryName(_path)!, "Unresolved Key"),
        };

    // A fresh pair each time, so every test goes through the file the way a restart does.
    private SeparationInterlock Interlock() => new(new JobStore(_path), _probe, Paths);

    /// <summary>A stem on disk as stemforge-cli writes one, tagged with the upload it came from.</summary>
    private string Stem(string name, string trackKey = TrackKey, DateTimeOffset? written = null)
    {
        Directory.CreateDirectory(Paths.StemsUnresolvedKey);

        var path = Path.Combine(Paths.StemsUnresolvedKey, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Comment =
                "stemforge/0.3.1 | preset: Vocal - Full | date: 2026-09-28 | "
                + $"source: https://www.youtube.com/watch?v={trackKey}";
            file.Save();
        }

        File.SetLastWriteTimeUtc(path, (written ?? Launched.AddMinutes(5)).UtcDateTime);

        return path;
    }

    [Fact]
    public void Nothing_is_live_before_anything_has_run()
    {
        Assert.False(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void A_claimed_separation_is_live()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        Assert.True(Interlock().TryGetLive(out var job));
        Assert.Equal(TrackKey, job.TrackKey);
    }

    [Fact]
    public void An_orphan_survives_a_restart_and_is_adopted_rather_than_relaunched()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        // crateside restarts; on Windows the child is not killed with its parent.
        var reconciliation = Interlock().Reconcile();

        Assert.Equal(TrackKey, Assert.Single(reconciliation.Adopted).TrackKey);
        Assert.Empty(reconciliation.Abandoned);
        Assert.True(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void An_adoption_is_recorded_on_the_job_and_keeps_the_process()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        Interlock().Reconcile();

        Assert.True(new JobStore(_path).TryGet(TrackKey, JobStage.Separation, out var job));
        Assert.NotNull(job.AdoptedAt);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(4242, job.ProcessId);
        Assert.Equal(Launched, job.ProcessStartedAt);
        Assert.True(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void An_adopted_run_that_is_gone_by_the_next_restart_is_abandoned()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched);
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
        Interlock().Claim(TrackKey, processId: 4242, Launched);

        Assert.True(new JobStore(_path).TryGet(TrackKey, JobStage.Separation, out var job));
        Assert.Null(job.AdoptedAt);
    }

    [Fact]
    public void A_process_that_died_without_recording_a_result_is_abandoned_and_strikes()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched);

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
        Interlock().Claim(TrackKey, processId: 4242, Launched);

        // Same id, different process: Windows handed 4242 to something else after the reboot.
        _probe.Running[4242] = Launched.AddHours(9);

        Assert.False(Interlock().TryGetLive(out _));
        Assert.Single(Interlock().Reconcile().Abandoned);
    }

    [Fact]
    public void A_live_pid_with_no_recorded_start_time_is_treated_as_ours()
    {
        // Written by an older crateside that did not record start times.
        new JobStore(_path).Record(
            TrackKey,
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
        Interlock().Claim(TrackKey, processId: 4242, Launched);
        _probe.Running[4242] = Launched;

        Interlock().Release(TrackKey);
        _probe.Running.Remove(4242);

        Assert.False(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void A_reported_failure_strikes_but_a_finished_run_does_not()
    {
        var interlock = Interlock();

        interlock.Claim(TrackKey, processId: 4242, Launched);
        Assert.Equal(1, interlock.Release(TrackKey, error: "cuda out of memory").Strikes);

        interlock.Claim(TrackKey, processId: 4243, Launched);
        Assert.Equal(JobStatus.Succeeded, interlock.Release(TrackKey).Status);
    }

    [Fact]
    public void A_separation_that_finished_while_crateside_was_down_is_recorded_as_succeeded()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched, ThreeStems);
        string[] stems =
        [
            Stem("Song (Vocal - Full).flac"),
            Stem("Song (Instrumental - Full).flac"),
            Stem("Song (Drums).flac"),
        ];

        // Restarted after the process ended on its own.
        var reconciliation = Interlock().Reconcile();

        var finished = Assert.Single(reconciliation.Finished);
        Assert.Equal(JobStatus.Succeeded, finished.Job.Status);
        Assert.Equal(stems.Order(), finished.OutputFiles.Order());
        Assert.Empty(reconciliation.Abandoned);
    }

    [Fact]
    public void A_separation_that_ended_short_is_abandoned_with_what_it_wrote()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched, ThreeStems);
        Stem("Song (Vocal - Full).flac");

        var abandoned = Assert.Single(Interlock().Reconcile().Abandoned);

        Assert.Equal(JobStatus.Failed, abandoned.Status);
        Assert.Contains("1 file of the 3", abandoned.Error);
    }

    [Fact]
    public void Stems_from_an_earlier_run_of_the_track_do_not_count()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched, ThreeStems);
        Stem("Song (Vocal - Full).flac", written: Launched.AddDays(-3));
        Stem("Song (Instrumental - Full).flac", written: Launched.AddDays(-3));
        Stem("Song (Drums).flac");

        Assert.Single(Interlock().Reconcile().Abandoned);
    }

    [Fact]
    public void Another_tracks_stems_do_not_count()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched, ThreeStems);
        Stem("Other (Vocal - Full).flac", trackKey: "otherVideo0");
        Stem("Other (Instrumental - Full).flac", trackKey: "otherVideo0");
        Stem("Other (Drums).flac", trackKey: "otherVideo0");

        Assert.Single(Interlock().Reconcile().Abandoned);
    }

    [Fact]
    public void An_adopted_run_keeps_its_promise_and_is_settled_when_it_ends()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched, ThreeStems);
        _probe.Running[4242] = Launched;
        Interlock().Reconcile();

        Assert.True(new JobStore(_path).TryGet(TrackKey, JobStage.Separation, out var adopted));
        Assert.Equal(ThreeStems, adopted.Promise);
        Assert.Empty(Interlock().SettleEnded().Finished);

        Stem("Song (Vocal - Full).flac");
        Stem("Song (Instrumental - Full).flac");
        Stem("Song (Drums).flac");
        _probe.Running.Remove(4242);

        var finished = Assert.Single(Interlock().SettleEnded().Finished);
        Assert.Equal(JobStatus.Succeeded, finished.Job.Status);
        Assert.False(Interlock().TryGetLive(out _));
    }

    [Fact]
    public void Reconciling_a_clean_startup_finds_nothing()
    {
        Interlock().Claim(TrackKey, processId: 4242, Launched);
        Interlock().Release(TrackKey);

        Assert.False(Interlock().Reconcile().FoundAnything);
    }
}
