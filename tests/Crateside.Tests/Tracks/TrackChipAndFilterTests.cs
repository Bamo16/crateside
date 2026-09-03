using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// The copy table, and the filters that replace the old sections.
/// </summary>
/// <remarks>
/// The chip and the <c>Next</c> sentence are the two halves of one table and both derive from
/// <see cref="TrackState"/>, so this asserts them together — the failure worth catching is one
/// drifting from the other, which no test of either alone would see.
/// </remarks>
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
                Samples = [new TrackSample { Label = "Vocal - Full", IsKeyTagged = true }],
            },
            TrackState.Parked => Base with { IsParkedForKey = true },
            TrackState.GaveUp => Base with { GaveUpAt = JobStage.Separation, GaveUpStrikes = 3 },
            TrackState.Held => Base with { IsHeld = true },
            TrackState.Done => Base with
            {
                Samples = [new TrackSample { Label = "Vocal - Full", IsWarped = true }],
            },
            _ => Base,
        };

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
    [InlineData(TrackState.Held, "held", TrackChipStyle.DashedOutline)]
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
        // The one chip that is not a fixed string, because "gave up" without saying where is not
        // actionable — the answer differs entirely between separation and tagging.
        var row = Row(TrackState.GaveUp);

        Assert.Equal("gave up · separation ×3", row.ChipLabel);
        Assert.Equal(TrackChipStyle.RedFilled, row.ChipStyle);
    }

    [Fact]
    public void Only_work_happening_this_second_carries_a_dot()
    {
        // Waiting in line for the GPU is teal, because the machine owns it, but nothing is
        // happening to that track yet and a pulsing dot would say otherwise.
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
    [InlineData(TrackState.Held, TrackFilter.Held)]
    [InlineData(TrackState.Done, TrackFilter.Done)]
    public void Every_state_lands_in_exactly_one_filter(TrackState state, TrackFilter expected)
    {
        // A partition, not overlapping tallies. A row in two filters would be counted twice on the
        // pills and would look like work that does not exist.
        var row = Row(state);
        var matched = Enum.GetValues<TrackFilter>()
            .Where(filter => filter is not TrackFilter.All && row.Matches(filter))
            .ToArray();

        Assert.Equal(expected, Assert.Single(matched));
        Assert.True(row.Matches(TrackFilter.All));
    }

    [Fact]
    public void A_finding_needs_attention_even_when_the_track_is_otherwise_finished()
    {
        // A stale tag is invisible everywhere else: the file looks done and pairs on the wrong
        // key. This is the only place it surfaces, so being done must not hide it.
        var row = Row(TrackState.Done) with
        {
            AuditNote = "tag disagrees",
        };

        Assert.True(row.Matches(TrackFilter.NeedsAttention));
        Assert.True(row.Matches(TrackFilter.Done));
    }

    [Fact]
    public void Counts_are_taken_over_the_whole_list()
    {
        TrackRow[] rows =
        [
            Row(TrackState.ReadyToWarp),
            Row(TrackState.ReadyToWarp),
            Row(TrackState.Separating),
            Row(TrackState.Held),
        ];

        Assert.Equal(4, rows.Count(TrackFilter.All));
        Assert.Equal(2, rows.Count(TrackFilter.InStaging));
        Assert.Equal(1, rows.Count(TrackFilter.InFlight));
        Assert.Equal(0, rows.Count(TrackFilter.Done));
    }
}
