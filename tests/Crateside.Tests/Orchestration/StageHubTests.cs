using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// The one place a stage says something happened.
/// </summary>
/// <remarks>
/// Stages published nothing at all, which is why the app appeared to need a refresh: the drain
/// controls raised <c>Changed</c> for pausing and for the activity line, so the header updated
/// live while the list underneath it went stale.
/// </remarks>
public class StageHubTests : IDisposable
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

    [Fact]
    public void A_stage_that_ran_reaches_a_subscriber()
    {
        var hub = new StageHub();
        StageChange? seen = null;

        hub.Changed += change => seen = change;
        hub.Publish(new StageChange { Stage = JobStage.Separation, VideoId = Video });

        Assert.Equal(JobStage.Separation, seen?.Stage);
        Assert.Equal(Video, seen?.VideoId);
    }

    [Fact]
    public void A_subscriber_falling_over_does_not_rob_the_others_or_the_publisher()
    {
        // The publisher is a drain loop and the subscribers are pages. A circuit that has gone
        // away throws from its handler, and letting that reach the loop would end a stage's tick
        // over a browser tab — or, worse, let the first subscriber registered decide whether the
        // second ever hears anything.
        var hub = new StageHub();
        var reached = false;

        hub.Changed += _ => throw new InvalidOperationException("a page that went away");
        hub.Changed += _ => reached = true;

        hub.Publish(new StageChange { Stage = JobStage.Tagging, VideoId = Video });

        Assert.True(reached);
    }

    [Fact]
    public void Every_recorded_job_announces_itself()
    {
        // Announced from the job store rather than from each drainer, because the store is the
        // single writer every stage already goes through. The same argument the queue view makes
        // for subtracting held tracks structurally: a guard that each of five callers has to
        // remember is one that a sixth will forget, and forgetting it is precisely the bug this
        // hub exists to fix.
        var hub = new StageHub();
        var seen = new List<StageChange>();

        hub.Changed += seen.Add;

        var jobs = new JobStore(_path, hub: hub);

        jobs.Record(Video, JobStage.Separation, JobStatus.Running);
        jobs.Record(Video, JobStage.Separation, JobStatus.Succeeded);

        Assert.Equal(2, seen.Count);
        Assert.All(seen, change => Assert.Equal(Video, change.VideoId));
        Assert.All(seen, change => Assert.Equal(JobStage.Separation, change.Stage));
    }

    [Fact]
    public void A_store_with_no_hub_still_records()
    {
        // Every test in this repo builds one without a hub, and so does anything reading the
        // store outside the app. Announcing is an extra, never a requirement.
        var jobs = new JobStore(_path);

        Assert.Equal(
            JobStatus.Running,
            jobs.Record(Video, JobStage.Evidence, JobStatus.Running).Status
        );
    }
}
