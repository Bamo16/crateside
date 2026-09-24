using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>The default order, and what it refuses to reorder.</summary>
public class TrackOrderTests
{
    private static TrackRow Row(string title, TrackState state = TrackState.Queued) =>
        state switch
        {
            TrackState.Detecting => Base(title) with { RunningStage = JobStage.Evidence },
            TrackState.Judging => Base(title) with { RunningStage = JobStage.Verdict },
            TrackState.Separating => Base(title) with { RunningStage = JobStage.Separation },
            TrackState.Tagging => Base(title) with { RunningStage = JobStage.Tagging },
            TrackState.Parked => Base(title) with { ParkedFor = ParkReason.NoKey },
            TrackState.GaveUp => Base(title) with
            {
                GaveUpAt = JobStage.Separation,
                GaveUpStrikes = 3,
            },
            TrackState.ReadyToWarp => Base(title) with
            {
                Samples = [new TrackSample { Label = "Vocal - Full", AbletonKey = "C minor" }],
            },
            TrackState.Done => Base(title) with
            {
                Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
            },
            _ => Base(title),
        };

    private static TrackRow Base(string title) => new() { VideoId = title, Title = title };

    private static IReadOnlyList<string> Ordered(params TrackRow[] rows) =>
        [.. TracksView.InOrder(rows).Select(row => row.Title)];

    [Fact]
    public void The_groups_run_attention_then_work_then_warping_then_done()
    {
        Assert.Equal(
            ["parked", "working", "warp", "finished"],
            Ordered(
                Row("finished", TrackState.Done),
                Row("warp", TrackState.ReadyToWarp),
                Row("working", TrackState.Separating),
                Row("parked", TrackState.Parked)
            )
        );
    }

    [Theory]
    [InlineData(TrackState.Detecting)]
    [InlineData(TrackState.Judging)]
    [InlineData(TrackState.Separating)]
    [InlineData(TrackState.Tagging)]
    public void Crossing_the_pipeline_does_not_move_a_track(TrackState state)
    {
        // Queued through tagging is one group.
        var before = Ordered(Row("a"), Row("b"), Row("c"));
        var after = Ordered(Row("a"), Row("b", state), Row("c"));

        Assert.Equal(before, after);
    }

    [Fact]
    public void The_working_group_keeps_the_order_it_arrived_in()
    {
        Assert.Equal(
            ["zebra", "apple", "mango"],
            Ordered(Row("zebra"), Row("apple"), Row("mango"))
        );
    }

    [Fact]
    public void Ready_to_warp_is_sorted_by_name()
    {
        Assert.Equal(
            ["Avril Lavigne", "Snail's House", "Starjunk 95", "星街すいせい"],
            Ordered(
                Row("Starjunk 95", TrackState.ReadyToWarp),
                Row("星街すいせい", TrackState.ReadyToWarp),
                Row("Avril Lavigne", TrackState.ReadyToWarp),
                Row("Snail's House", TrackState.ReadyToWarp)
            )
        );
    }

    [Fact]
    public void A_track_carrying_a_finding_is_wanted_whatever_state_it_is_in()
    {
        var ordered = Ordered(
            Row("clean", TrackState.ReadyToWarp),
            Row("flagged", TrackState.ReadyToWarp) with
            {
                AuditNote = "tag disagrees",
            }
        );

        Assert.Equal(["flagged", "clean"], ordered);
    }

    [Fact]
    public void The_librarys_own_tracks_are_reachable_under_All_and_nowhere_else()
    {
        var library = Row("old", TrackState.Done) with { IsLibraryOnly = true };

        Assert.True(library.Matches(TrackFilter.All));
        Assert.False(library.Matches(TrackFilter.Unfinished));

        var queued = Row("recent", TrackState.Done);

        Assert.True(queued.Matches(TrackFilter.All));
        Assert.False(queued.Matches(TrackFilter.Unfinished));
    }
}
