using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Looks a track up on the sites by its artist and title, once, and keeps what it found beside the evidence (ADR 0076).</summary>
public sealed class PageLookups(ISourcePages pages, WorkingSet workingSet, LedgerStore store)
{
    public const string SourcesFileName = "sources.json";

    /// <summary>The track's pages from the working set's copy, else fetched and kept there unless Hooktheory or Musicnotes could not be reached.</summary>
    public async Task<IReadOnlyList<SiteLookup>> LookUpAsync(
        WorkOrder order,
        CancellationToken ct = default
    )
    {
        var path = Path.Combine(workingSet.Prepare(order.VideoId), SourcesFileName);

        if (
            SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.ReadFileOrDefault(path) is
            { } cached
        )
            return cached;

        if (NamesOf(order) is not var (artist, title))
            return [];

        var lookups = await pages.FetchAsync(artist, title, ct);

        if (!lookups.Any(VerdictRules.MayHoldAnotherKey))
            SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.WriteFile(path, lookups);

        return lookups;
    }

    /// <summary>The ledger's names for a song judged before, else the row's <c>Artist - Title</c>.</summary>
    public (string Artist, string Title)? NamesOf(WorkOrder order) =>
        HeldRun(order) is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? (artist, title)
        : order.Title.Split(" - ", 2) is [var named, var rest]
        && rest.Trim() is { Length: > 0 } rowTitle
            ? (named.Trim(), rowTitle)
        : null;

    /// <summary>The run already recorded for this upload, or for the source it was swapped to.</summary>
    public LedgerRun? HeldRun(WorkOrder order)
    {
        var ledger = store.Read();

        return YouTubeUrl.TryGetVideoId(order.SourceUrl, out var source)
            && ledger.TryGetLatestByVideoId(source, out var swapped)
                ? swapped
            : ledger.TryGetLatestByVideoId(order.VideoId, out var held) ? held
            : null;
    }
}
