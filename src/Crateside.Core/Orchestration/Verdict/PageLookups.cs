using Crateside.Core.Ledger;
using Crateside.Core.Naming;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Looks a track up on the sites by its artist and title, once, and keeps what it found beside the evidence (ADR 0076).</summary>
public sealed class PageLookups(
    ISourcePages pages,
    WorkingSet workingSet,
    LedgerStore store,
    ResolvedNameStore? names = null
)
{
    public const string SourcesFileName = "sources.json";

    /// <summary>The track's pages from the working set's copy, else fetched and kept there unless Hooktheory or Musicnotes could not be reached.</summary>
    public async Task<IReadOnlyList<SiteLookup>> LookUpAsync(
        WorkOrder order,
        CancellationToken ct = default
    )
    {
        if (CachedFor(order) is { } cached)
            return cached;

        if (NamesOf(order) is not var (artist, title))
            return [];

        var lookups = await pages.FetchAsync(artist, title, ct);

        if (!lookups.Any(VerdictRules.MayHoldAnotherKey))
            SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.WriteFile(
                Path.Combine(workingSet.Prepare(order.VideoId), SourcesFileName),
                lookups
            );

        return lookups;
    }

    /// <summary>What an earlier lookup kept for this track, or null when there is none.</summary>
    public IReadOnlyList<SiteLookup>? CachedFor(WorkOrder order) =>
        SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.ReadFileOrDefault(
            Path.Combine(
                Path.GetDirectoryName(workingSet.EvidencePathFor(order.VideoId))!,
                SourcesFileName
            )
        );

    /// <summary>The ledger's names for a song judged before, else the row's <c>Artist - Title</c>, else StemForge's name for a title that names no artist (a <c>Topic</c> upload's).</summary>
    public (string Artist, string Title)? NamesOf(WorkOrder order) =>
        HeldRun(order) is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? (artist, title)
            : Split(order.Title)
                ?? (
                    names?.Read().GetValueOrDefault(order.VideoId) is { DisplayTitle: var resolved }
                        ? Split(resolved)
                        : null
                );

    private static (string Artist, string Title)? Split(string name) =>
        name.Split(" - ", 2) is [var named, var rest]
        && named.Trim() is { Length: > 0 } artist
        && rest.Trim() is { Length: > 0 } title
            ? (artist, title)
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
