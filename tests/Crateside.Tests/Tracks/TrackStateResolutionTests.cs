using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// Which state a track is in, and the sentence that says what happens next.
/// </summary>
/// <remarks>
/// The chip and the <c>Next</c> column both read these, which is the point: a track's state was
/// previously implicit in which bucket it fell into, spread across the queue view's work lists,
/// the job store, and which folder its files were in. Deriving it once is what keeps the two
/// columns from drifting apart.
/// </remarks>
public class TrackStateResolutionTests
{
    private const string Video = "aaaaaaaaaaa";

    private static TrackRow Row() =>
        new()
        {
            VideoId = Video,
            PlaylistItemId = $"PLI-{Video}",
            Title = "Track one",
        };

    private static TrackSample Sample(string label, bool tagged = true, bool moved = false) =>
        new()
        {
            Label = label,
            AbletonKey = tagged ? "C minor" : null,
            IsInLibrary = moved,
        };

    [Fact]
    public void A_track_nothing_has_run_for_is_queued()
    {
        var row = Row();

        Assert.Equal(TrackState.Queued, row.State);
        Assert.Equal("downloading next", row.Next);
    }

    [Fact]
    public void A_track_whose_evidence_run_is_in_flight_is_detecting()
    {
        var row = Row() with { RunningStage = JobStage.Evidence };

        Assert.Equal(TrackState.Detecting, row.State);
        Assert.Equal("then judging", row.Next);
    }

    [Theory]
    [InlineData(JobStage.Evidence, TrackState.Detecting)]
    [InlineData(JobStage.Verdict, TrackState.Judging)]
    [InlineData(JobStage.Separation, TrackState.Separating)]
    [InlineData(JobStage.Handoff, TrackState.Staging)]
    [InlineData(JobStage.Tagging, TrackState.Tagging)]
    public void A_stage_in_flight_is_the_state(JobStage running, TrackState expected)
    {
        // The five stages map onto five states one-for-one. Worth pinning as a table rather than
        // trusting the switch to stay exhaustive: a stage added later with no state to land on
        // would otherwise silently read as "queued", which is the one state that means nothing
        // has run.
        Assert.Equal(expected, (Row() with { RunningStage = running }).State);
    }

    [Fact]
    public void A_track_whose_samples_are_staged_and_tagged_is_ready_to_warp()
    {
        // The state the "In staging" filter exists for, and the one that fires the toast. A track
        // sits here for weeks, so it is a resting place and carries no urgency.
        var row = Row() with
        {
            Samples = [Sample("Vocal - Full"), Sample("Instrumental - Full")],
        };

        Assert.Equal(TrackState.ReadyToWarp, row.State);
        Assert.Equal("your turn — warp, then move to Mashup Samples", row.Next);
    }

    [Fact]
    public void A_track_whose_samples_have_all_reached_Mashup_Samples_is_done()
    {
        // Presence in Mashup Samples is what says a sample is finished: the user invokes the move
        // in Ableton and Ableton moves the file on disk. Nothing is marked done by hand, and
        // there is no ".asd" reading anywhere near this — see ADR 0002 for why that signal was
        // deleted rather than downgraded.
        var row = Row() with
        {
            Samples =
            [
                Sample("Vocal - Full", moved: true),
                Sample("Instrumental - Full", moved: true),
            ],
        };

        Assert.Equal(TrackState.Done, row.State);
        Assert.Equal("in Mashup Samples", row.Next);
    }

    [Fact]
    public void A_track_staged_but_not_yet_tagged_is_tagging()
    {
        // Nothing is running for it, but it is in the tagging stage's work list — that stage
        // contends for nothing, so it will be picked up unattended rather than waiting on a turn.
        var row = Row() with
        {
            Samples = [Sample("Vocal - Full", tagged: false)],
        };

        Assert.Equal(TrackState.Tagging, row.State);
        Assert.Equal("writing Ableton key tags", row.Next);
    }

    [Fact]
    public void Moving_one_sample_out_leaves_the_track_the_users_turn()
    {
        // The case that makes the warped reading per-sample rather than per-track: the user
        // empties staging progressively, so a half-moved track is still work in front of them.
        var row = Row() with
        {
            Samples = [Sample("Vocal - Full", moved: true), Sample("Instrumental - Full")],
        };

        Assert.Equal(TrackState.ReadyToWarp, row.State);
    }

    [Fact]
    public void A_sample_already_in_Mashup_Samples_is_not_waiting_to_be_tagged()
    {
        // crateside never writes tags in Mashup Samples — the user tags by hand there, and the
        // move covers warping, tagging and renaming together. So a moved sample must not hold the
        // track back in "tagging" for want of a tag crateside was never going to apply.
        var row = Row() with
        {
            Samples =
            [
                Sample("Vocal - Full", tagged: false, moved: true),
                Sample("Instrumental - Full"),
            ],
        };

        Assert.Equal(TrackState.ReadyToWarp, row.State);
    }

    [Fact]
    public void A_track_separated_without_a_key_worth_tagging_is_parked()
    {
        // Not a failure and not a queue: a LOW or WITHHELD verdict sends stems to the
        // unresolved-key root and the handoff stage deliberately leaves them there, because
        // staging is where tagging happens and there is nothing to tag them with.
        var row = Row() with
        {
            IsParkedForKey = true,
        };

        Assert.Equal(TrackState.Parked, row.State);
        Assert.Equal("needs a better verdict — nothing automatic will move it", row.Next);
    }

    [Fact]
    public void A_stage_out_of_strikes_has_given_up()
    {
        // Tagging included. The queue page only ever checked evidence, verdict, separation and
        // handoff, so a track that gave up while tagging said nothing at all.
        var row = Row() with
        {
            GaveUpAt = JobStage.Tagging,
            GaveUpStrikes = 3,
        };

        Assert.Equal(TrackState.GaveUp, row.State);
        Assert.Equal("retry, or take it out of the playlist", row.Next);
    }

    [Theory]
    [InlineData(1, "next on the GPU")]
    [InlineData(2, "2nd for the GPU")]
    [InlineData(3, "3rd for the GPU")]
    public void A_judged_track_waiting_on_the_gpu_says_where_it_is_in_line(
        int position,
        string expected
    )
    {
        var row = Row() with { GpuPosition = position };

        Assert.Equal(TrackState.JudgedInLine, row.State);
        Assert.Equal(expected, row.Next);
    }
}
