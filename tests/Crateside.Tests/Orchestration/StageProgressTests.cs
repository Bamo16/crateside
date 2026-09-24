using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// What the job store says about one track, reduced to the two facts a row needs: which stage
/// has it in flight, and whether any stage has stopped trying.
/// </summary>
/// <remarks>
/// Assembled ad hoc in <c>Queue.razor</c> before this, over four of the five stages — tagging was
/// missing, so a track that gave up there said nothing at all.
/// </remarks>
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
    public void A_stage_out_of_strikes_has_given_up_and_tagging_counts()
    {
        // Tagging is the stage the queue page never checked. It fails for a real reason — Ableton
        // holding the tag store — so a track can genuinely exhaust it and then say nothing.
        var jobs = Store();

        for (var attempt = 0; attempt < TaggingSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Tagging, JobStatus.Failed, error: "Ableton had the file");

        var progress = StageProgress.For(jobs, Video);

        Assert.Equal(JobStage.Tagging, progress.GaveUpAt);
        Assert.Equal(TaggingSelector.DefaultMaxStrikes, progress.GaveUpStrikes);
    }

    [Fact]
    public void Evidence_is_allowed_more_tries_than_the_rest()
    {
        // The same count that ends separation leaves evidence still going, because acquiring
        // audio fails for transient reasons the other stages do not share. Copying the limit
        // beside this logic instead of reading it off the selector is how the two would drift.
        var jobs = Store();

        for (var attempt = 0; attempt < SeparationSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Evidence, JobStatus.Failed, error: "download failed");

        Assert.Null(StageProgress.For(jobs, Video).GaveUpAt);
    }

    [Fact]
    public void Putting_a_track_back_in_line_undoes_giving_up()
    {
        // Strikes never decrease, so forgiveness cannot work by clearing the count. Every
        // selector reads Cancelled as "eligible again, whatever the count", and this has to agree
        // or the row would keep saying "gave up" about a track already back in the work list.
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
