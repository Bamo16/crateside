using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Tagging;

namespace Crateside.Tests.Orchestration;

/// <summary>What the job store says about one track, reduced to what a row needs.</summary>
public class StageProgressTests : IDisposable
{
    private const string Video = "aaaaaaaaaaa";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "jobs.jsonl"
    );

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private JobStore Store() => new(_path);

    [Fact]
    public void A_stage_in_flight_is_reported_running()
    {
        var jobs = Store();
        jobs.Record(Video, JobStage.Separation, JobStatus.Running);

        Assert.Equal(JobStage.Separation, StageProgress.For(jobs, Video).Running);
    }

    [Fact]
    public void An_adopted_separation_is_running_since_its_process_started_not_since_the_restart()
    {
        var launched = new DateTimeOffset(2026, 9, 23, 20, 0, 0, TimeSpan.Zero);
        var jobs = Store();
        jobs.Record(Video, JobStage.Separation, JobStatus.Running, 4242, launched);
        jobs.Record(Video, JobStage.Separation, JobStatus.Running, 4242, launched, adopted: true);

        Assert.Equal(launched, StageProgress.For(jobs, Video).RunningSince);
    }

    [Fact]
    public void A_failure_with_strikes_left_is_reported_as_retrying_with_its_reason()
    {
        var jobs = Store();
        jobs.Record(Video, JobStage.Separation, JobStatus.Failed, error: "cuda out of memory");

        var progress = StageProgress.For(jobs, Video);

        Assert.Equal(
            new StageFailure(JobStage.Separation, 1, "cuda out of memory"),
            progress.Retrying
        );
        Assert.Null(progress.GaveUpAt);
    }

    [Fact]
    public void A_stage_that_has_given_up_is_not_also_retrying()
    {
        var jobs = Store();

        for (var attempt = 0; attempt < TaggingSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Tagging, JobStatus.Failed, error: "Ableton had the file");

        Assert.Null(StageProgress.For(jobs, Video).Retrying);
    }

    [Fact]
    public void A_failure_put_back_in_line_by_hand_is_not_retrying()
    {
        var jobs = Store();
        jobs.Record(Video, JobStage.Download, JobStatus.Failed, error: "403");
        jobs.Forgive(Video, JobStage.Download);

        Assert.Null(StageProgress.For(jobs, Video).Retrying);
    }

    [Fact]
    public void A_stage_out_of_strikes_has_given_up_and_tagging_counts()
    {
        var jobs = Store();

        for (var attempt = 0; attempt < TaggingSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Tagging, JobStatus.Failed, error: "Ableton had the file");

        var progress = StageProgress.For(jobs, Video);

        Assert.Equal(JobStage.Tagging, progress.GaveUpAt);
        Assert.Equal(TaggingSelector.DefaultMaxStrikes, progress.GaveUpStrikes);
    }

    [Fact]
    public void An_upload_youtube_will_never_serve_gives_up_at_once_saying_why()
    {
        var jobs = Store();
        jobs.Record(
            Video,
            JobStage.Download,
            JobStatus.Failed,
            error: "ERROR: [youtube] aaaaaaaaaaa: Video unavailable. This video is not available"
        );

        var progress = StageProgress.For(jobs, Video);

        Assert.Equal(JobStage.Download, progress.GaveUpAt);
        Assert.Equal("upload unavailable", progress.GaveUpFor);
        Assert.Null(progress.Retrying);
    }

    [Fact]
    public void Evidence_is_allowed_more_tries_than_the_rest()
    {
        var jobs = Store();

        for (var attempt = 0; attempt < SeparationSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Evidence, JobStatus.Failed, error: "download failed");

        Assert.Null(StageProgress.For(jobs, Video).GaveUpAt);
    }

    [Fact]
    public void Putting_a_track_back_in_line_undoes_giving_up()
    {
        // Strikes never decrease; every selector reads Cancelled as eligible again.
        var jobs = Store();

        for (var attempt = 0; attempt < SeparationSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Separation, JobStatus.Failed, error: "the GPU said no");

        jobs.Forgive(Video, JobStage.Separation);

        Assert.Null(StageProgress.For(jobs, Video).GaveUpAt);
    }

    [Fact]
    public void A_track_nothing_has_run_for_reports_nothing()
    {
        Assert.Equal(new StageProgress(), StageProgress.For(Store(), Video));
    }
}
