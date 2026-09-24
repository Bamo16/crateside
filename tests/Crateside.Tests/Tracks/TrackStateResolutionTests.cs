using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>Which state a track is in, and the sentence that says what happens next.</summary>
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
        // A table, so a stage added later with no state to land on fails here rather than reading
        // as "queued".
        Assert.Equal(expected, (Row() with { RunningStage = running }).State);
    }

    [Fact]
    public void A_track_whose_samples_are_staged_and_tagged_is_ready_to_warp()
    {
        var row = Row() with { Samples = [Sample("Vocal - Full"), Sample("Instrumental - Full")] };

        Assert.Equal(TrackState.ReadyToWarp, row.State);
        Assert.Equal("your turn — warp, then move to Mashup Samples", row.Next);
    }

    [Fact]
    public void A_track_whose_samples_have_all_reached_Mashup_Samples_is_done()
    {
        // Presence in Mashup Samples is what says a sample is finished (ADR 0002).
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
        var row = Row() with { Samples = [Sample("Vocal - Full", tagged: false)] };

        Assert.Equal(TrackState.Tagging, row.State);
        Assert.Equal("writing Ableton key tags", row.Next);
    }

    [Fact]
    public void Moving_one_sample_out_leaves_the_track_the_users_turn()
    {
        // Half moved: still work in front of the user.
        var row = Row() with
        {
            Samples = [Sample("Vocal - Full", moved: true), Sample("Instrumental - Full")],
        };

        Assert.Equal(TrackState.ReadyToWarp, row.State);
    }

    [Fact]
    public void A_sample_already_in_Mashup_Samples_is_not_waiting_to_be_tagged()
    {
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
        var row = Row() with { ParkedFor = ParkReason.NoKey };

        Assert.Equal(TrackState.Parked, row.State);
        Assert.Equal("needs a better verdict — nothing automatic will move it", row.Next);
        Assert.Equal("parked · no key", row.ChipLabel);
    }

    [Fact]
    public void A_track_whose_download_missed_premium_audio_is_parked_over_its_source()
    {
        var row = Row() with
        {
            ParkedFor = ParkReason.PremiumShortfall,
            Acquisition = new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder },
        };

        Assert.Equal(TrackState.Parked, row.State);
        Assert.Equal("check for a better upload, or use this one anyway", row.Next);
        Assert.Equal("parked · poor audio", row.ChipLabel);
        Assert.Contains("song version exists", row.Detail);
    }

    [Fact]
    public void Parking_over_the_source_leaves_the_separation_plan_open()
    {
        Assert.True((Row() with { ParkedFor = ParkReason.PremiumShortfall }).CanChangePlan);
        Assert.False((Row() with { ParkedFor = ParkReason.NoKey }).CanChangePlan);
    }

    [Fact]
    public void A_stage_out_of_strikes_has_given_up()
    {
        var row = Row() with { GaveUpAt = JobStage.Tagging, GaveUpStrikes = 3 };

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
