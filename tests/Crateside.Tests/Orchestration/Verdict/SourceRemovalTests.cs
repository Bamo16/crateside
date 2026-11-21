using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class SourceRemovalTests : IDisposable
{
    private const string Track = "dQw4w9WgXcQ";
    private const string Article = "https://example.com/article-on-the-song/";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-07T12:00:00+00:00");

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    public void Dispose() => _ledger.Dispose();

    private static RunEvidence Run() =>
        new()
        {
            Timestamp = At,
            Song = "a - b",
            Sources =
            [
                new()
                {
                    Url = Article,
                    StatesKey = "A minor",
                    VerdictKey = "A minor",
                },
                new()
                {
                    Url = "https://example.org/other",
                    StatesKey = "A minor",
                    VerdictKey = "A minor",
                },
                new()
                {
                    Via = "web search",
                    StatesKey = "A minor",
                    VerdictKey = "A minor",
                },
            ],
        };

    [Fact]
    public void A_removed_source_is_marked_and_the_others_are_not()
    {
        var store = new PageChoiceStore(_ledger);

        Assert.True(store.RemoveSource(Track, Article.TrimEnd('/'), At));

        var marked = store.Marked(Track, Run());

        Assert.Equal([true, false, false], marked.Sources.Select(source => source.IsRemoved));
        Assert.Equal("2 of 2 sources agree; no detector readings.", RunSummary.Of(marked));
    }

    [Fact]
    public void A_removal_survives_a_restart_and_only_touches_its_own_track()
    {
        new PageChoiceStore(_ledger).RemoveSource(Track, Article, At);

        var reread = new PageChoiceStore(_ledger);

        Assert.Equal([Article], reread.RemovedFor(Track));
        Assert.Empty(reread.RemovedFor("another"));
        Assert.True(reread.Marked(Track, Run()).Sources[0].IsRemoved);
    }

    [Fact]
    public void Restoring_a_source_counts_it_again()
    {
        var store = new PageChoiceStore(_ledger);

        store.RemoveSource(Track, Article, At);

        Assert.True(store.RestoreSource(Track, Article, At.AddMinutes(1)));
        Assert.False(store.Marked(Track, Run()).Sources[0].IsRemoved);
        Assert.False(store.RestoreSource(Track, Article, At.AddMinutes(2)));
        Assert.Equal(At.AddMinutes(1), store.LastChange(Track));
    }

    [Fact]
    public void A_source_found_only_by_how_it_was_found_can_be_removed()
    {
        var store = new PageChoiceStore(_ledger);

        store.RemoveSource(Track, "web search", At);

        Assert.Equal(
            [false, false, true],
            store.Marked(Track, Run()).Sources.Select(source => source.IsRemoved)
        );
    }

    [Fact]
    public void Removing_a_source_leaves_the_page_choices_in_the_same_file_alone()
    {
        var store = new PageChoiceStore(_ledger);
        var page = new FetchedPage { Site = "Hooktheory", Url = Article };

        store.Choose(
            Track,
            new PageChoice
            {
                Url = page.Url,
                Kind = PageChoiceKind.Rejected,
                Page = page,
                At = At,
            }
        );
        store.RemoveSource(Track, Article, At);

        var reread = new PageChoiceStore(_ledger);

        Assert.Single(reread.ActiveFor(Track));
        Assert.Equal([Article], reread.RemovedFor(Track));
    }
}
