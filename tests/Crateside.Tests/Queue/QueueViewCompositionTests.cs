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
        IReadOnlySet<string>? separatedWithoutKey = null,
        params TrackInstruction[] instructions
    ) =>
        QueueView.Compose(
            items,
            fullyProcessed ?? new HashSet<string>(),
            verdictWithoutStems ?? new HashSet<string>(),
            separatedWithoutKey ?? new HashSet<string>(),
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
    public void A_track_parked_for_want_of_a_key_is_not_awaiting_separation()
    {
        // It has a verdict and no stems in staging, which is the exact test for "awaiting
        // stems" -- so it sat in that list permanently, reading as work the GPU had not got to
        // when the GPU had already done it. The user held tracks by hand to work around this.
        var view = Compose(
            [Item(Judged)],
            verdictWithoutStems: new HashSet<string> { Judged },
            separatedWithoutKey: new HashSet<string> { Judged }
        );

        Assert.Empty(view.AwaitingSeparation);
        Assert.Equal(Judged, Assert.Single(view.AwaitingKey).VideoId);

        // Still not processed: its stems are parked, not staged, so the queue keeps it and the
        // janitor keeps its working set.
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
        const string Parked = "eeeeeeeeeee";

        var view = Compose(
            [Item(Judged), Item(Unjudged), Item(Done), Item(Parked)],
            fullyProcessed: new HashSet<string> { Done },
            verdictWithoutStems: new HashSet<string> { Judged, Parked },
            separatedWithoutKey: new HashSet<string> { Parked }
        );

        // A parked track is in verdictWithoutStems too, so a new list that failed to subtract
        // itself from the old one would show up here as a track counted twice.
        Assert.Equal(
            view.Items.Count,
            view.Outstanding.Count
                + view.AwaitingSeparation.Count
                + view.AwaitingKey.Count
                + view.FullyProcessed.Count
        );
    }
}
