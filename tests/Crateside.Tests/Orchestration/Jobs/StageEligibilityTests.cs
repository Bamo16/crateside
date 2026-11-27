using Crateside.Core.Orchestration.Jobs;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class StageEligibilityTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly JobStore _jobs;

    public StageEligibilityTests() =>
        _jobs = new(Path.Combine(_folder, "jobs.jsonl"), new FakeTimeProvider());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private SkipCause? WhyNot(JobStage stage, int maxStrikes = 3, bool retrySucceeded = false) =>
        StageEligibility.WhyNot(_jobs, TrackKey, stage, maxStrikes, retrySucceeded);

    private void Fail(JobStage stage, int times, string error = "boom")
    {
        for (var i = 0; i < times; i++)
            _jobs.Record(TrackKey, stage, JobStatus.Failed, error: error);
    }

    [Fact]
    public void A_stage_never_run_may_be_attempted()
    {
        Assert.Null(WhyNot(JobStage.Evidence));
        Assert.False(StageEligibility.Owes(_jobs, TrackKey, JobStage.Evidence));
    }

    [Theory]
    [InlineData(JobStatus.Queued, SkipCause.InFlight)]
    [InlineData(JobStatus.Running, SkipCause.InFlight)]
    [InlineData(JobStatus.AwaitingPerson, SkipCause.AwaitingPerson)]
    [InlineData(JobStatus.Succeeded, SkipCause.AlreadyDone)]
    [InlineData(JobStatus.Canceled, null)]
    [InlineData(JobStatus.Held, null)]
    public void A_recorded_status_decides_whether_the_stage_may_try_again(
        JobStatus status,
        SkipCause? expected
    )
    {
        _jobs.Record(TrackKey, JobStage.Verdict, status);

        Assert.Equal(expected, WhyNot(JobStage.Verdict));
    }

    [Fact]
    public void A_past_success_is_attempted_again_only_where_the_stage_asks_for_it()
    {
        _jobs.Record(TrackKey, JobStage.Tagging, JobStatus.Succeeded);

        Assert.Equal(SkipCause.AlreadyDone, WhyNot(JobStage.Tagging, retrySucceeded: false));
        Assert.Null(WhyNot(JobStage.Tagging, retrySucceeded: true));
    }

    [Fact]
    public void A_failed_stage_is_tried_again_until_its_strikes_run_out()
    {
        Fail(JobStage.Handoff, times: 2);
        Assert.Null(WhyNot(JobStage.Handoff, maxStrikes: 3));

        Fail(JobStage.Handoff, times: 1);
        Assert.Equal(SkipCause.GaveUp, WhyNot(JobStage.Handoff, maxStrikes: 3));
    }

    [Fact]
    public void A_job_put_back_in_line_by_hand_is_owed_again_and_may_be_attempted()
    {
        Fail(JobStage.Handoff, times: 2);
        _jobs.Forgive(TrackKey, JobStage.Handoff);

        Assert.Null(WhyNot(JobStage.Handoff, maxStrikes: 3));
        Assert.True(StageEligibility.Owes(_jobs, TrackKey, JobStage.Handoff));
    }

    [Fact]
    public void A_download_youtube_will_not_serve_is_failed_for_good_on_the_first_strike()
    {
        Fail(JobStage.Download, times: 1, error: "ERROR: Private video. Sign in");

        Assert.Equal(SkipCause.FailedPermanently, WhyNot(JobStage.Download, maxStrikes: 5));
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Download, out var job));
        Assert.True(StageEligibility.IsPermanent(job, out var reason));
        Assert.Equal("upload private", reason);
        Assert.True(StageEligibility.HasGivenUp(job));
        Assert.False(StageEligibility.IsOwed(job));
    }

    [Fact]
    public void Only_a_download_can_fail_for_good()
    {
        Fail(JobStage.Evidence, times: 1, error: "ERROR: Private video");

        Assert.True(_jobs.TryGet(TrackKey, JobStage.Evidence, out var job));
        Assert.False(StageEligibility.IsPermanent(job, out var reason));
        Assert.Null(reason);
        Assert.Null(WhyNot(JobStage.Evidence, maxStrikes: 3));
    }

    [Theory]
    [InlineData(JobStatus.Queued, true)]
    [InlineData(JobStatus.Running, true)]
    [InlineData(JobStatus.Canceled, true)]
    [InlineData(JobStatus.Held, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.AwaitingPerson, false)]
    public void Work_is_owed_while_it_is_open_and_not_once_done_or_with_the_person(
        JobStatus status,
        bool owed
    )
    {
        var job = _jobs.Record(TrackKey, JobStage.Separation, status);

        Assert.Equal(owed, StageEligibility.IsOwed(job));
        Assert.Equal(owed, StageEligibility.Owes(_jobs, TrackKey, JobStage.Separation));
    }

    [Fact]
    public void A_failed_job_is_owed_until_it_gives_up()
    {
        var limit = StageEligibility.MaxStrikesFor(JobStage.Separation);

        Fail(JobStage.Separation, times: limit - 1);
        Assert.True(StageEligibility.Owes(_jobs, TrackKey, JobStage.Separation));

        Fail(JobStage.Separation, times: 1);
        Assert.False(StageEligibility.Owes(_jobs, TrackKey, JobStage.Separation));
    }

    [Theory]
    [InlineData(JobStage.Download, 5)]
    [InlineData(JobStage.Evidence, 5)]
    [InlineData(JobStage.Verdict, 3)]
    [InlineData(JobStage.Separation, 3)]
    [InlineData(JobStage.Handoff, 3)]
    [InlineData(JobStage.Tagging, 3)]
    public void Each_stage_has_its_own_strike_limit(JobStage stage, int expected)
    {
        Assert.Equal(expected, StageEligibility.MaxStrikesFor(stage));
    }
}
