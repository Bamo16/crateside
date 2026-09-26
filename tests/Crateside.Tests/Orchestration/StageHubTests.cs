using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Orchestration;

/// <summary>The one place a stage says something happened.</summary>
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
        // A circuit that has gone away throws from its handler.
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
        var jobs = new JobStore(_path);

        Assert.Equal(
            JobStatus.Running,
            jobs.Record(Video, JobStage.Evidence, JobStatus.Running).Status
        );
    }
}
