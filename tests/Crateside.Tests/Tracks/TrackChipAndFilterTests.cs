using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>The chip and <c>Next</c> copy, asserted together, and the filters.</summary>
public class TrackChipAndFilterTests
{
    private static TrackRow Row(TrackState state) =>
        state switch
        {
            TrackState.Queued => Base,
            TrackState.Detecting => Base with { RunningStage = JobStage.Evidence },
            TrackState.Judging => Base with { RunningStage = JobStage.Verdict },
            TrackState.JudgedInLine => Base with { GpuPosition = 1 },
            TrackState.Separating => Base with { RunningStage = JobStage.Separation },
            TrackState.Staging => Base with { RunningStage = JobStage.Handoff },
            TrackState.Tagging => Base with { RunningStage = JobStage.Tagging },
            TrackState.ReadyToWarp => Base with
            {
                Samples = [new TrackSample { Label = "Vocal - Full", AbletonKey = "C minor" }],
            },
            TrackState.Parked => Base with { ParkedFor = ParkReason.NoKey },
            TrackState.GaveUp => Base with { GaveUpAt = JobStage.Separation, GaveUpStrikes = 3 },
            TrackState.Done => Base with
            {
                Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
            },
            _ => Base,
        };

    [Fact]
    public void A_swapped_track_waiting_for_the_GPU_is_not_done()
    {
        // Its old samples are all in the library while its replacement is queued.
        var row = Base with
        {
            Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
            GpuPosition = 1,
        };

        Assert.Equal(TrackState.JudgedInLine, row.State);
        Assert.True(row.Matches(TrackFilter.InFlight));
        Assert.True(row.Matches(TrackFilter.Unfinished));
    }

    [Fact]
    public void A_swapped_track_that_is_separating_is_not_done_either()
    {
        var row = Base with
        {
            Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
            RunningStage = JobStage.Separation,
        };

        Assert.Equal(TrackState.Separating, row.State);
    }

    private static TrackRow Base =>
        new()
        {
            VideoId = "aaaaaaaaaaa",
            PlaylistItemId = "PLI-a",
            Title = "Rob Dougan - Furious Angels",
        };

    [Theory]
    [InlineData(TrackState.Queued, "queued", TrackChipStyle.NeutralOutline)]
    [InlineData(TrackState.Detecting, "detecting key", TrackChipStyle.TealFilled)]
    [InlineData(TrackState.Judging, "judging", TrackChipStyle.TealFilled)]
    [InlineData(TrackState.JudgedInLine, "judged · in line", TrackChipStyle.TealOutline)]
    [InlineData(TrackState.Separating, "separating", TrackChipStyle.TealFilled)]
    [InlineData(TrackState.Staging, "staging", TrackChipStyle.TealFilled)]
    [InlineData(TrackState.Tagging, "tagging", TrackChipStyle.TealFilled)]
    [InlineData(TrackState.ReadyToWarp, "ready to warp", TrackChipStyle.AmberFilled)]
    [InlineData(TrackState.Parked, "parked · no key", TrackChipStyle.RedOutline)]
    [InlineData(TrackState.Done, "done", TrackChipStyle.GreenOutline)]
    public void The_chip_says_what_the_copy_table_says(
        TrackState state,
        string label,
        TrackChipStyle style
    )
    {
        var row = Row(state);

        Assert.Equal(state, row.State);
        Assert.Equal(label, row.ChipLabel);
        Assert.Equal(style, row.ChipStyle);
    }

    [Fact]
    public void Giving_up_names_the_stage_and_counts_the_tries()
    {
        var row = Row(TrackState.GaveUp);

        Assert.Equal("gave up · separation ×3", row.ChipLabel);
        Assert.Equal(TrackChipStyle.RedFilled, row.ChipStyle);
    }

    [Fact]
    public void Only_work_happening_this_second_carries_a_dot()
    {
        Assert.True(Row(TrackState.Separating).ChipHasDot);
        Assert.False(Row(TrackState.JudgedInLine).ChipHasDot);
        Assert.False(Row(TrackState.ReadyToWarp).ChipHasDot);
    }

    [Theory]
    [InlineData(TrackState.Queued, TrackFilter.InFlight)]
    [InlineData(TrackState.Detecting, TrackFilter.InFlight)]
    [InlineData(TrackState.JudgedInLine, TrackFilter.InFlight)]
    [InlineData(TrackState.Tagging, TrackFilter.InFlight)]
    [InlineData(TrackState.ReadyToWarp, TrackFilter.InStaging)]
    [InlineData(TrackState.Parked, TrackFilter.NeedsAttention)]
    [InlineData(TrackState.GaveUp, TrackFilter.NeedsAttention)]
    public void Every_unfinished_state_lands_in_exactly_one_named_view(
        TrackState state,
        TrackFilter expected
    )
    {
        var row = Row(state);
        var matched = Named.Where(row.Matches).ToArray();

        Assert.Equal(expected, Assert.Single(matched));
        Assert.True(row.Matches(TrackFilter.Unfinished));
        Assert.True(row.Matches(TrackFilter.All));
    }

    [Fact]
    public void A_finished_track_is_in_no_named_view_and_is_not_unfinished()
    {
        var row = Row(TrackState.Done);

        Assert.All(Named, filter => Assert.False(row.Matches(filter)));
        Assert.False(row.Matches(TrackFilter.Unfinished));
        Assert.True(row.Matches(TrackFilter.All));
    }

    private static readonly TrackFilter[] Named =
    [
        TrackFilter.InStaging,
        TrackFilter.InFlight,
        TrackFilter.NeedsAttention,
    ];

    [Fact]
    public void A_finding_needs_attention_even_when_the_track_is_otherwise_finished()
    {
        var row = Row(TrackState.Done) with { AuditNote = "tag disagrees" };

        Assert.True(row.Matches(TrackFilter.NeedsAttention));

        Assert.False(row.Matches(TrackFilter.Unfinished));
    }

    [Fact]
    public void A_staged_track_awaiting_a_rename_is_in_two_views_at_once()
    {
        var row = Row(TrackState.ReadyToWarp) with { AwaitingRename = 1 };

        Assert.True(row.Matches(TrackFilter.InStaging));
        Assert.True(row.Matches(TrackFilter.NeedsAttention));
    }

    [Fact]
    public void Counts_are_taken_over_the_whole_list()
    {
        TrackRow[] rows =
        [
            Row(TrackState.ReadyToWarp),
            Row(TrackState.ReadyToWarp),
            Row(TrackState.Separating),
            Row(TrackState.Done),
        ];

        Assert.Equal(4, rows.Count(TrackFilter.All));
        Assert.Equal(3, rows.Count(TrackFilter.Unfinished));
        Assert.Equal(2, rows.Count(TrackFilter.InStaging));
        Assert.Equal(1, rows.Count(TrackFilter.InFlight));
    }
}
