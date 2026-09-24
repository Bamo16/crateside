using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>Work for a track the playlist no longer holds: GPU, never quota.</summary>
public class ReopenedTrackTests
{
    private const string Queued = "aaaaaaaaaaa";
    private const string Reopened = "bbbbbbbbbbb";

    private static QueueItem Item(string videoId) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = "In the playlist",
        };

    private static QueueItem Off(string videoId) =>
        new() { VideoId = videoId, Title = "Long since removed" };

    private static QueueView Compose(params QueueItem[] reopened) =>
        QueueView.Compose(
            [Item(Queued)],
            fullyProcessed: new HashSet<string>(),
            // Both have a verdict and no stems, which is what a re-cut leaves behind.
            verdictWithoutStems: new HashSet<string> { Queued, Reopened },
            separatedWithoutKey: new HashSet<string>(),
            instructions: new Dictionary<string, TrackInstruction>(),
            reopened: reopened
        );

    [Fact]
    public void A_reopened_track_joins_the_separation_work_list()
    {
        var view = Compose(Off(Reopened));

        Assert.Contains(view.AwaitingSeparation, item => item.VideoId == Reopened);
    }

    [Fact]
    public void It_never_reaches_the_lists_that_spend_quota()
    {
        // Kept out of Items, which Outstanding is built from, so a re-cut cannot buy a second judging.
        var view = Compose(Off(Reopened));

        Assert.DoesNotContain(view.Items, item => item.VideoId == Reopened);
        Assert.DoesNotContain(view.Outstanding, item => item.VideoId == Reopened);
    }

    [Fact]
    public void It_is_not_offered_for_removal()
    {
        var view = Compose(Off(Reopened));

        Assert.DoesNotContain(view.FullyProcessed, item => item.VideoId == Reopened);
        Assert.Null(
            Assert.Single(view.AwaitingSeparation, i => i.VideoId == Reopened).PlaylistItemId
        );
    }

    [Fact]
    public void The_playlist_still_answers_for_itself()
    {
        var view = Compose();

        Assert.Contains(view.AwaitingSeparation, item => item.VideoId == Queued);
        Assert.Single(view.AwaitingSeparation);
    }

    [Fact]
    public void One_owing_only_a_handoff_keeps_its_row_without_joining_the_gpu_line()
    {
        // Separation succeeded and it owes only a handoff (#32).
        var view = QueueView.Compose(
            [Item(Queued)],
            fullyProcessed: new HashSet<string>(),
            verdictWithoutStems: new HashSet<string> { Queued, Reopened },
            separatedWithoutKey: new HashSet<string>(),
            instructions: new Dictionary<string, TrackInstruction>(),
            reopened: [Off(Reopened)],
            // It owes a stage, but not this one.
            awaitingCut: new HashSet<string>()
        );

        Assert.DoesNotContain(view.AwaitingSeparation, item => item.VideoId == Reopened);

        Assert.Contains(view.Reopened, item => item.VideoId == Reopened);
        Assert.Contains(view.Everything, item => item.VideoId == Reopened);
    }

    [Fact]
    public void One_parked_for_want_of_a_key_is_not_in_the_gpu_line_either()
    {
        var view = QueueView.Compose(
            [Item(Queued)],
            fullyProcessed: new HashSet<string>(),
            verdictWithoutStems: new HashSet<string> { Queued },
            separatedWithoutKey: new HashSet<string> { Reopened },
            instructions: new Dictionary<string, TrackInstruction>(),
            reopened: [Off(Reopened)]
        );

        Assert.DoesNotContain(view.AwaitingSeparation, item => item.VideoId == Reopened);
    }
}
