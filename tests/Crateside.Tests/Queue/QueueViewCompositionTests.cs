using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// The sorting of a playlist into the work lists each stage reads. Split out of
/// <c>QueueService</c> so it can be exercised without a queue credential or a disk scan, which
/// is why none of these rules had a test before.
/// </summary>
public class QueueViewCompositionTests
{
    private const string Judged = "aaaaaaaaaaa";
    private const string Unjudged = "bbbbbbbbbbb";
    private const string Done = "ccccccccccc";

    private static QueueItem Item(string videoId) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = $"Track {videoId}",
        };

    private static QueueView Compose(
        IReadOnlyList<QueueItem> items,
        IReadOnlySet<string>? fullyProcessed = null,
        IReadOnlySet<string>? verdictWithoutStems = null,
        params TrackInstruction[] instructions
    ) =>
        QueueView.Compose(
            items,
            fullyProcessed ?? new HashSet<string>(),
            verdictWithoutStems ?? new HashSet<string>(),
            instructions.ToDictionary(t => t.VideoId, StringComparer.Ordinal)
        );

    [Fact]
    public void A_track_with_no_verdict_is_outstanding_and_nothing_else()
    {
        var view = Compose([Item(Unjudged)]);

        Assert.Equal(Unjudged, Assert.Single(view.Outstanding).VideoId);
        Assert.Empty(view.AwaitingSeparation);
        Assert.Empty(view.FullyProcessed);
    }

    [Fact]
    public void A_verdict_without_stems_awaits_separation_and_leaves_outstanding()
    {
        // These two mean opposite things about a verdict, which is what made gating Outstanding
        // on "has a verdict" yield nothing at all.
        var view = Compose([Item(Judged)], verdictWithoutStems: new HashSet<string> { Judged });

        Assert.Equal(Judged, Assert.Single(view.AwaitingSeparation).VideoId);
        Assert.Empty(view.Outstanding);
    }

    [Fact]
    public void A_verdict_with_stems_is_done_and_in_no_work_list()
    {
        var view = Compose([Item(Done)], fullyProcessed: new HashSet<string> { Done });

        Assert.Equal(Done, Assert.Single(view.FullyProcessed).VideoId);
        Assert.Empty(view.Outstanding);
        Assert.Empty(view.AwaitingSeparation);
    }

    [Fact]
    public void A_held_track_is_absent_from_every_work_list()
    {
        // The guard has to be impossible to forget rather than merely applied everywhere today,
        // which is why holds are subtracted here instead of checked by each stage.
        var view = Compose(
            [Item(Unjudged)],
            instructions: new TrackInstruction { VideoId = Unjudged, IsHeld = true }
        );

        Assert.Equal(Unjudged, Assert.Single(view.Held).VideoId);
        Assert.Empty(view.Outstanding);
        Assert.Empty(view.AwaitingSeparation);
    }

    [Fact]
    public void A_held_track_that_has_been_judged_still_cannot_reach_the_gpu()
    {
        // AwaitingSeparation never subtracted holds, because nothing read it at the time. Once
        // the separation selector started reading it, that was a hole straight to the GPU.
        var view = Compose(
            [Item(Judged)],
            verdictWithoutStems: new HashSet<string> { Judged },
            instructions: new TrackInstruction { VideoId = Judged, IsHeld = true }
        );

        Assert.Empty(view.AwaitingSeparation);
        Assert.Single(view.Held);
    }

    [Fact]
    public void The_instrumental_release_fact_is_carried_onto_the_item()
    {
        // Carried rather than looked up again, so composing a work order stays pure. This is the
        // whole path from the user's click to the recipe: if it does not land here, the toggle
        // silently does nothing and a vocal stem gets made anyway.
        var view = Compose(
            [Item(Unjudged)],
            instructions: new TrackInstruction { VideoId = Unjudged, IsInstrumentalRelease = true }
        );

        Assert.True(Assert.Single(view.Outstanding).IsInstrumentalRelease);
    }

    [Fact]
    public void A_track_nobody_has_spoken_about_says_nothing_about_vocals()
    {
        Assert.False(Assert.Single(Compose([Item(Unjudged)]).Outstanding).IsInstrumentalRelease);
    }

    [Fact]
    public void An_instrumental_release_can_still_be_marked_while_it_awaits_separation()
    {
        // The last chance to say it: after separation its stems exist, and the wrong ones cannot
        // be un-made.
        var view = Compose(
            [Item(Judged)],
            verdictWithoutStems: new HashSet<string> { Judged },
            instructions: new TrackInstruction { VideoId = Judged, IsInstrumentalRelease = true }
        );

        Assert.True(Assert.Single(view.AwaitingSeparation).IsInstrumentalRelease);
    }

    [Fact]
    public void Every_item_is_in_exactly_one_bucket()
    {
        var view = Compose(
            [Item(Judged), Item(Unjudged), Item(Done), Item("ddddddddddd")],
            fullyProcessed: new HashSet<string> { Done },
            verdictWithoutStems: new HashSet<string> { Judged },
            instructions: new TrackInstruction { VideoId = "ddddddddddd", IsHeld = true }
        );

        Assert.Equal(
            view.Items.Count,
            view.Outstanding.Count
                + view.AwaitingSeparation.Count
                + view.FullyProcessed.Count
                + view.Held.Count
        );
    }
}
