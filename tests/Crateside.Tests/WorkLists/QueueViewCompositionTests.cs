using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.WorkLists;

/// <summary>The sorting of a playlist into the work lists each stage reads.</summary>
public sealed class QueueViewCompositionTests
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
        var view = Compose(
            [Item(Judged)],
            verdictWithoutStems: new HashSet<string> { Judged },
            separatedWithoutKey: new HashSet<string> { Judged }
        );

        Assert.Empty(view.AwaitingSeparation);
        Assert.Equal(Judged, Assert.Single(view.AwaitingKey).VideoId);

        Assert.Empty(view.FullyProcessed);
    }

    [Fact]
    public void A_verdict_without_stems_awaits_separation_and_leaves_outstanding()
    {
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
        var view = Compose(
            [Item(Unjudged)],
            instructions: new TrackInstruction
            {
                VideoId = Unjudged,
                Plan = StemPlan.InstrumentalRelease,
            }
        );

        Assert.True(
            Assert.Single(view.Outstanding).Plan.AsksTheSameAs(StemPlan.InstrumentalRelease)
        );
    }

    [Fact]
    public void A_track_nobody_has_spoken_about_says_nothing_about_vocals()
    {
        Assert.True(
            Assert
                .Single(Compose([Item(Unjudged)]).Outstanding)
                .Plan.AsksTheSameAs(StemPlan.Default)
        );
    }

    [Fact]
    public void An_instrumental_release_can_still_be_marked_while_it_awaits_separation()
    {
        var view = Compose(
            [Item(Judged)],
            verdictWithoutStems: new HashSet<string> { Judged },
            instructions: new TrackInstruction
            {
                VideoId = Judged,
                Plan = StemPlan.InstrumentalRelease,
            }
        );

        Assert.True(
            Assert.Single(view.AwaitingSeparation).Plan.AsksTheSameAs(StemPlan.InstrumentalRelease)
        );
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

        // A parked track is in verdictWithoutStems too, so this catches it being counted twice.
        Assert.Equal(
            view.Items.Count,
            view.Outstanding.Count
                + view.AwaitingSeparation.Count
                + view.AwaitingKey.Count
                + view.FullyProcessed.Count
        );
    }

    [Fact]
    public void A_track_the_playlist_still_holds_reaches_separation_when_a_cut_is_owed()
    {
        // An additive re-cut: its stems are still on disk, so it is not in verdictWithoutStems.
        var view = QueueView.Compose(
            [Item(Judged)],
            fullyProcessed: new HashSet<string> { Judged },
            verdictWithoutStems: new HashSet<string>(),
            separatedWithoutKey: new HashSet<string>(),
            instructions: new Dictionary<string, TrackInstruction>(),
            awaitingCut: new HashSet<string> { Judged }
        );

        Assert.Equal(Judged, Assert.Single(view.AwaitingSeparation).VideoId);
    }

    [Fact]
    public void A_cut_owed_for_a_track_parked_for_want_of_a_key_is_still_refused()
    {
        var view = QueueView.Compose(
            [Item(Judged)],
            fullyProcessed: new HashSet<string>(),
            verdictWithoutStems: new HashSet<string> { Judged },
            separatedWithoutKey: new HashSet<string> { Judged },
            instructions: new Dictionary<string, TrackInstruction>(),
            awaitingCut: new HashSet<string> { Judged }
        );

        Assert.Empty(view.AwaitingSeparation);
    }

    [Fact]
    public void What_a_track_already_has_reaches_the_item_that_asks_for_more()
    {
        var inventory = StemInventory.Of(
            [
                new StemOnDisk
                {
                    Path = @"P:\Staging\x (Vocal - Full).flac",
                    Root = @"P:\Staging",
                    Preset = "Vocal - Full",
                },
            ],
            PresetCatalogReader.Parse(
                """[{"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"}]"""
            )
        );

        var view = QueueView.Compose(
            [Item(Judged)],
            fullyProcessed: new HashSet<string>(),
            verdictWithoutStems: new HashSet<string> { Judged },
            separatedWithoutKey: new HashSet<string>(),
            instructions: new Dictionary<string, TrackInstruction>(),
            inventories: new Dictionary<string, StemInventory> { [Judged] = inventory }
        );

        var item = Assert.Single(view.AwaitingSeparation);

        Assert.True(item.Plan.AsksTheSameAs(StemPlan.Default));
        Assert.Equal(["instrumental_full"], item.Inventory.Shortfall(item.Plan).Presets);
    }
}
