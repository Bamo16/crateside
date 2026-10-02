using Crateside.Core.WorkLists;

namespace Crateside.Tests.WorkLists;

/// <summary>Putting one track at the front of the work lists (ADR 0015).</summary>
public sealed class BumpedTrackTests
{
    private const string First = "aaaaaaaaaaa";
    private const string Second = "bbbbbbbbbbb";
    private const string Third = "ccccccccccc";

    [Fact]
    public void Nothing_is_bumped_to_begin_with() => Assert.Null(new BumpedTrack().VideoId);

    [Fact]
    public void Bumping_another_track_replaces_the_first()
    {
        var bumped = new BumpedTrack();

        bumped.Bump(First);
        bumped.Bump(Second);

        Assert.Equal(Second, bumped.VideoId);
    }

    [Fact]
    public void Clearing_gives_up_the_bump()
    {
        var bumped = new BumpedTrack();
        bumped.Bump(First);

        bumped.Clear();

        Assert.Null(bumped.VideoId);
    }

    [Fact]
    public void The_bumped_track_leads_the_work_list()
    {
        var view = Compose(bumped: Third);

        Assert.Equal([Third, First, Second], Ids(view.Outstanding));
    }

    [Fact]
    public void Everything_else_keeps_its_place()
    {
        var view = Compose(bumped: Second);

        Assert.Equal([Second, First, Third], Ids(view.Outstanding));
    }

    [Fact]
    public void No_bump_leaves_the_order_alone()
    {
        Assert.Equal([First, Second, Third], Ids(Compose().Outstanding));
        Assert.Equal([First, Second, Third], Ids(Compose(bumped: string.Empty).Outstanding));
    }

    [Fact]
    public void Bumping_a_track_the_list_does_not_hold_is_harmless() =>
        Assert.Equal([First, Second, Third], Ids(Compose(bumped: "zzzzzzzzzzz").Outstanding));

    [Fact]
    public void The_separation_list_is_bumped_too()
    {
        var view = WorkListsView.Compose(
            [Item(First), Item(Second), Item(Third)],
            new HashSet<string>(),
            new HashSet<string> { First, Second, Third },
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>(),
            bumped: Third
        );

        Assert.Equal([Third, First, Second], Ids(view.AwaitingSeparation));
    }

    private static WorkListsView Compose(string? bumped = null) =>
        WorkListsView.Compose(
            [Item(First), Item(Second), Item(Third)],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>(),
            bumped: bumped
        );

    private static WorkItem Item(string videoId) =>
        new()
        {
            VideoId = videoId,
            PlaylistItemId = $"PLI-{videoId}",
            Title = videoId,
        };

    private static IReadOnlyList<string> Ids(IEnumerable<WorkItem> items) =>
        [.. items.Select(item => item.VideoId)];
}
