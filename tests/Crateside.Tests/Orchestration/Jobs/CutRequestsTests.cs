using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration.Jobs;

/// <summary>A re-cut or swap is a request, answered by the outcomes after it (#50).</summary>
public class CutRequestsTests
{
    private static readonly DateTimeOffset Asked = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static TrackJob Job(
        JobStage stage,
        JobStatus status,
        DateTimeOffset at,
        int strikes = 0
    ) =>
        new()
        {
            Id = $"aaaaaaaaaaa:{stage}",
            VideoId = "aaaaaaaaaaa",
            Stage = stage,
            Status = status,
            UpdatedAt = at,
            Strikes = strikes,
        };

    [Fact]
    public void Nothing_asked_owes_nothing()
    {
        Assert.False(CutRequests.OwesCut(requestedAt: null, separation: null));
        Assert.True(CutRequests.IsSettled(requestedAt: null, handoff: null));
    }

    [Fact]
    public void A_success_before_the_request_does_not_answer_it()
    {
        var before = Job(JobStage.Separation, JobStatus.Succeeded, Asked.AddHours(-1));

        Assert.True(CutRequests.OwesCut(Asked, before));
    }

    [Fact]
    public void A_success_after_the_request_answers_it()
    {
        var after = Job(JobStage.Separation, JobStatus.Succeeded, Asked.AddMinutes(7));

        Assert.False(CutRequests.OwesCut(Asked, after));
    }

    [Fact]
    public void A_separation_in_hand_is_never_started_twice()
    {
        var running = Job(JobStage.Separation, JobStatus.Running, Asked.AddHours(-1));

        Assert.False(CutRequests.OwesCut(Asked, running));
    }

    [Fact]
    public void A_track_that_gave_up_before_the_request_is_asked_again()
    {
        var gaveUp = Job(JobStage.Separation, JobStatus.Failed, Asked.AddHours(-1), strikes: 3);

        Assert.True(CutRequests.OwesCut(Asked, gaveUp));
    }

    [Fact]
    public void A_failure_since_the_request_is_left_to_the_strikes()
    {
        var failed = Job(JobStage.Separation, JobStatus.Failed, Asked.AddMinutes(3), strikes: 1);

        Assert.False(CutRequests.OwesCut(Asked, failed));
    }

    [Fact]
    public void Only_a_handoff_since_the_request_settles_it()
    {
        Assert.False(CutRequests.IsSettled(Asked, handoff: null));
        Assert.False(
            CutRequests.IsSettled(
                Asked,
                Job(JobStage.Handoff, JobStatus.Succeeded, Asked.AddHours(-1))
            )
        );
        Assert.False(
            CutRequests.IsSettled(
                Asked,
                Job(JobStage.Handoff, JobStatus.Failed, Asked.AddMinutes(9))
            )
        );
        Assert.True(
            CutRequests.IsSettled(
                Asked,
                Job(JobStage.Handoff, JobStatus.Succeeded, Asked.AddMinutes(9))
            )
        );
    }
}
