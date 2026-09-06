using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// Work for a track the playlist no longer holds.
/// </summary>
/// <remarks>
/// A track leaves the playlist as soon as it is processed, and re-cutting one creates real work
/// long afterwards. Separation is the only stage that reads the queue rather than the disk, so it
/// was the only one that could not see such a track — the files were deleted and nothing ever
/// picked it up. What matters most here is where these tracks are allowed to appear: the one list
/// that spends GPU, and never the one that spends quota.
/// </remarks>
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

    /// <summary>A track with work to redo and no playlist entry — which is the whole point.</summary>
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
        // The guard that matters. Evidence and the verdict both read Outstanding, and Outstanding
        // is built from Items -- so keeping these out of Items is what makes it impossible for a
        // re-cut to buy a second judging, rather than merely unlikely.
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
        // Nothing about the ordinary path changes: a queued track with a verdict and no stems is
        // awaiting separation exactly as before.
        var view = Compose();

        Assert.Contains(view.AwaitingSeparation, item => item.VideoId == Queued);
        Assert.Single(view.AwaitingSeparation);
    }
}
