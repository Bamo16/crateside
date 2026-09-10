using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// The default order, and — more to the point — what it refuses to reorder.
/// </summary>
/// <remarks>
/// The page was built on rows never moving, because what it replaced bucketed tracks into sections
/// and shuffled them between the two while the user was trying to click one. Grouping brings some
/// of that back, so the group boundaries are drawn at exactly the three transitions worth
/// noticing: reaching <em>ready to warp</em>, being parked, and being finished with. Everything
/// else has to leave a row where it is, and that is what most of these assert.
/// </remarks>
public class TrackOrderTests
{
    private static TrackRow Row(string title, TrackState state = TrackState.Queued) =>
        state switch
        {
            TrackState.Detecting => Base(title) with { RunningStage = JobStage.Evidence },
            TrackState.Judging => Base(title) with { RunningStage = JobStage.Verdict },
            TrackState.Separating => Base(title) with { RunningStage = JobStage.Separation },
            TrackState.Tagging => Base(title) with { RunningStage = JobStage.Tagging },
            TrackState.Held => Base(title) with { IsHeld = true },
            TrackState.Parked => Base(title) with { IsParkedForKey = true },
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

    /// <summary>The ordering as the composition applies it, without the whole join around it.</summary>
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

    [Fact]
    public void Holding_a_track_does_not_move_it()
    {
        // The one that mattered most: a hold is a change the user makes, and the row jumping out
        // from under the press was the worst of what this page replaced.
        var before = Ordered(Row("a"), Row("b"), Row("c"));
        var after = Ordered(Row("a"), Row("b", TrackState.Held), Row("c"));

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(TrackState.Detecting)]
    [InlineData(TrackState.Judging)]
    [InlineData(TrackState.Separating)]
    [InlineData(TrackState.Tagging)]
    public void Crossing_the_pipeline_does_not_move_a_track(TrackState state)
    {
        // Queued through tagging is one group, so a track works its way across without the row
        // shifting once.
        var before = Ordered(Row("a"), Row("b"), Row("c"));
        var after = Ordered(Row("a"), Row("b", state), Row("c"));

        Assert.Equal(before, after);
    }

    [Fact]
    public void The_working_group_keeps_the_order_it_arrived_in()
    {
        // Which is the playlist's own oldest-first order — the order the stages will take them.
        // Sorting the groups separately is what preserves it.
        Assert.Equal(
            ["zebra", "apple", "mango"],
            Ordered(Row("zebra"), Row("apple", TrackState.Held), Row("mango"))
        );
    }

    [Fact]
    public void Ready_to_warp_is_sorted_by_name()
    {
        // Ableton's browser lists Sample Staging by name, and this is the list read beside it.
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

        // A finished track crateside did queue is in exactly the same views. Where it came from
        // stopped deciding what it is reachable through when All began meaning all.
        var queued = Row("recent", TrackState.Done);

        Assert.True(queued.Matches(TrackFilter.All));
        Assert.False(queued.Matches(TrackFilter.Unfinished));
    }
}
