using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>The chip and <c>Next</c> copy, asserted together, and the filters.</summary>
public sealed class TrackChipAndFilterTests
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
            TrackState.ReadyToMove => Base with
            {
                Samples =
                [
                    new TrackSample
                    {
                        Label = "Vocal - Full",
                        AbletonKey = "C minor",
                        HasDefaultClip = true,
                    },
                ],
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
            TrackKey = "aaaaaaaaaaa",
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
    [InlineData(TrackState.ReadyToMove, "ready to move", TrackChipStyle.AmberFilled)]
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
    public void An_upload_that_can_never_be_fetched_says_why_instead_of_counting()
    {
        var row = Base with
        {
            GaveUpAt = JobStage.Download,
            GaveUpStrikes = 1,
            GaveUpFor = "upload unavailable",
            IsLibraryOnly = true,
        };

        Assert.Equal("gave up · upload unavailable", row.ChipLabel);
        Assert.Contains("It cannot be judged from this upload.", row.Detail);
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
    [InlineData(TrackState.ReadyToMove, TrackFilter.InStaging)]
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

    private static readonly DateTimeOffset LookedUp = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private static TrackRow Tagged(GenreRecord? genre, FamiliarityRecord? familiarity) =>
        Base with
        {
            Genre = genre,
            Familiarity = familiarity,
        };

    private static GenreRecord Genre(bool settled) =>
        new()
        {
            Song = "rob dougan - furious angels",
            Genre = settled ? "Electronic" : null,
            IsSettled = settled,
            Suggested = "Electronic",
            LookedUpAt = LookedUp,
        };

    private static FamiliarityRecord Familiarity(long? fans) =>
        new()
        {
            Song = "rob dougan - furious angels",
            ArtistFans = fans,
            LookedUpAt = LookedUp,
        };

    [Fact]
    public void An_unanswered_genre_is_a_tag_to_answer() =>
        Assert.True(
            Tagged(Genre(settled: false), Familiarity(1_000_000)).Matches(TrackFilter.TagsToAnswer)
        );

    [Fact]
    public void A_familiarity_the_numbers_leave_to_the_user_is_a_tag_to_answer() =>
        Assert.True(
            Tagged(Genre(settled: true), Familiarity(fans: null)).Matches(TrackFilter.TagsToAnswer)
        );

    [Fact]
    public void A_settled_genre_and_a_derived_familiarity_leave_nothing_to_answer() =>
        Assert.False(
            Tagged(Genre(settled: true), Familiarity(1_000_000)).Matches(TrackFilter.TagsToAnswer)
        );

    [Fact]
    public void A_song_nothing_has_looked_up_has_nothing_to_answer_yet() =>
        Assert.False(Tagged(genre: null, familiarity: null).Matches(TrackFilter.TagsToAnswer));

    [Fact]
    public void A_verdict_names_who_made_it_in_the_detail_line()
    {
        static TrackRow By(DecidedBy decided) =>
            Base with
            {
                Verdict = new LedgerRun
                {
                    Timestamp = DateTimeOffset.Parse("2026-11-03T00:00:00+00:00"),
                    Song = "rob dougan - furious angels",
                    Confidence = KeyConfidence.High,
                    Center = new TonalCenter("A", DiatonicMode.Aeolian),
                    DecidedBy = decided,
                },
            };

        Assert.Equal("Your call: A minor.", By(DecidedBy.Person).Detail);
        Assert.Equal("Ruled A minor by crateside, high confidence.", By(DecidedBy.Rules).Detail);
        Assert.Equal("Judged A minor, high confidence.", By(DecidedBy.Judge).Detail);
        Assert.Equal(DecidedBy.Person, By(DecidedBy.Person).DecidedBy);
    }
}
