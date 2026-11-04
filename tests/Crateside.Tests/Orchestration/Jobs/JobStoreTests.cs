using Crateside.Core.Orchestration.Jobs;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class JobStoreTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "jobs.jsonl"
    );

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 8, 13, 22, 0, 0, TimeSpan.Zero)
    );

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private JobStore Store() => new(_path, _time);

    [Fact]
    public void A_stage_never_run_is_simply_absent()
    {
        Assert.False(Store().TryGet(TrackKey, JobStage.Separation, out _));
    }

    [Fact]
    public void The_latest_update_wins_for_a_job()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Evidence, JobStatus.Queued);
        _time.Advance(TimeSpan.FromMinutes(1));
        store.Record(TrackKey, JobStage.Evidence, JobStatus.Running);
        _time.Advance(TimeSpan.FromMinutes(1));
        store.Record(TrackKey, JobStage.Evidence, JobStatus.Succeeded);

        Assert.True(store.TryGet(TrackKey, JobStage.Evidence, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void A_summary_survives_a_restart_and_a_line_written_without_one_still_reads()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            $$$"""{"id":"{{{TrackKey}}}:Evidence","videoId":"{{{TrackKey}}}","stage":"Evidence","status":"Succeeded","timestamp":"2026-08-13T21:00:00+00:00"}"""
                + Environment.NewLine
        );
        Store().Record(TrackKey, JobStage.Tagging, JobStatus.Succeeded, summary: "tagged D maj");

        var history = Store().History(TrackKey);

        Assert.Null(history[0].Summary);
        Assert.Equal("tagged D maj", history[1].Summary);
    }

    [Fact]
    public void State_survives_a_restart()
    {
        Store().Record(TrackKey, JobStage.Separation, JobStatus.Running, processId: 4242);

        Assert.True(Store().TryGet(TrackKey, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(4242, job.ProcessId);
    }

    [Fact]
    public void Retrying_does_not_mint_a_second_job()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Verdict, JobStatus.Failed, error: "quota exhausted");
        store.Record(TrackKey, JobStage.Verdict, JobStatus.Running);
        store.Record(TrackKey, JobStage.Verdict, JobStatus.Succeeded);

        Assert.Single(store.All());
    }

    [Fact]
    public void Strikes_count_failures_across_the_whole_history()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Verdict, JobStatus.Failed, error: "quota exhausted");
        store.Record(TrackKey, JobStage.Verdict, JobStatus.Failed, error: "quota exhausted");
        var job = store.Record(TrackKey, JobStage.Verdict, JobStatus.Failed, error: "again");

        Assert.Equal(3, job.Strikes);

        Assert.True(Store().TryGet(TrackKey, JobStage.Verdict, out var reloaded));
        Assert.Equal(3, reloaded.Strikes);
    }

    [Fact]
    public void Canceling_is_not_a_strike()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Separation, JobStatus.Running);
        var job = store.Record(TrackKey, JobStage.Separation, JobStatus.Canceled);

        Assert.Equal(0, job.Strikes);
    }

    [Fact]
    public void A_held_attempt_is_no_strike_and_is_still_owed_and_selectable_and_survives_a_reload()
    {
        var store = Store();
        var until = _time.GetUtcNow().AddMinutes(5);

        var job = store.Record(
            TrackKey,
            JobStage.Verdict,
            JobStatus.Held,
            error: "rate limited",
            heldUntil: until
        );

        Assert.Equal(0, job.Strikes);
        Assert.True(StageEligibility.IsOwed(job));
        Assert.True(StageEligibility.Allows(store, TrackKey, JobStage.Verdict, 3, false));
        Assert.Equal(
            until,
            Assert.Single(Store().Updates(), update => update.Status is JobStatus.Held).HeldUntil
        );
    }

    [Fact]
    public void A_job_waiting_for_the_person_carries_no_strike_and_is_nobodys_to_retry()
    {
        var store = Store();

        var job = store.Record(TrackKey, JobStage.Verdict, JobStatus.AwaitingPerson);

        Assert.Equal(0, job.Strikes);
        Assert.False(StageEligibility.IsOwed(job));
        Assert.False(StageEligibility.Allows(store, TrackKey, JobStage.Verdict, 3, true));
        Assert.False(job.IsOpen);
    }

    [Fact]
    public void A_failure_message_stays_visible_through_a_retry_and_clears_on_success()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Verdict, JobStatus.Failed, error: "quota exhausted");

        Assert.Equal(
            "quota exhausted",
            store.Record(TrackKey, JobStage.Verdict, JobStatus.Running).Error
        );
        Assert.Null(store.Record(TrackKey, JobStage.Verdict, JobStatus.Succeeded).Error);
    }

    [Fact]
    public void The_stages_of_one_track_are_separate_jobs()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Evidence, JobStatus.Succeeded);
        store.Record(TrackKey, JobStage.Separation, JobStatus.Running);

        Assert.Equal(2, store.All().Count);
        Assert.True(store.TryGet(TrackKey, JobStage.Evidence, out var evidence));
        Assert.Equal(JobStatus.Succeeded, evidence.Status);
        Assert.True(store.TryGet(TrackKey, JobStage.Separation, out var separation));
        Assert.Equal(JobStatus.Running, separation.Status);
    }

    [Fact]
    public void Open_finds_what_outlived_the_process_that_started_it()
    {
        var store = Store();

        store.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Running, processId: 111);
        store.Record("bbbbbbbbbbb", JobStage.Separation, JobStatus.Succeeded);
        store.Record("ccccccccccc", JobStage.Verdict, JobStatus.Running);

        var open = Store().Open(JobStage.Separation);

        Assert.Equal("aaaaaaaaaaa", Assert.Single(open).TrackKey);
    }

    [Fact]
    public void A_half_written_trailing_line_does_not_lose_the_rest()
    {
        Store().Record(TrackKey, JobStage.Evidence, JobStatus.Succeeded);

        File.AppendAllText(_path, "{\"id\":\"trunc");

        Assert.True(Store().TryGet(TrackKey, JobStage.Evidence, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void The_file_is_append_only()
    {
        var store = Store();

        store.Record(TrackKey, JobStage.Evidence, JobStatus.Queued);
        store.Record(TrackKey, JobStage.Evidence, JobStatus.Running);
        store.Record(TrackKey, JobStage.Evidence, JobStatus.Succeeded);

        Assert.Equal(3, File.ReadAllLines(_path).Length);
    }

    [Fact]
    public void Statuses_are_written_as_names_so_the_file_reads_at_3am()
    {
        Store().Record(TrackKey, JobStage.Separation, JobStatus.Running, processId: 4242);

        var line = File.ReadAllText(_path);

        Assert.Contains("\"Separation\"", line);
        Assert.Contains("\"Running\"", line);
        Assert.DoesNotContain("error", line, StringComparison.OrdinalIgnoreCase);
    }
}
