using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Playlist;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Looks a track up on the sites by its artist and title, once, and keeps what it found beside the evidence (ADR 0076).</summary>
public sealed class PageLookups(
    ISourcePages pages,
    WorkingSet workingSet,
    LedgerStore store,
    ResolvedNameStore? names = null,
    IntakeStore? intake = null,
    PageChoiceStore? choices = null
)
{
    public const string SourcesFileName = "sources.json";

    private static readonly string[] SiteNames = ["Hooktheory", "Musicnotes", "Ultimate Guitar"];

    private readonly HashSet<string> _retried = [];

    /// <summary>The track's pages from the working set's copy, else fetched and kept there with whatever could not be reached; the rule pass retries those once a start (ADR 0096).</summary>
    public async Task<IReadOnlyList<SiteLookup>> LookUpAsync(
        WorkOrder order,
        CancellationToken ct = default
    ) => await LookUpAsync(order, Asker.Rules, ct);

    /// <summary>The same, for the judge's session: a site the rule pass still could not reach is tried once more (ADR 0096).</summary>
    public Task<IReadOnlyList<SiteLookup>> LookUpForJudgeAsync(
        WorkOrder order,
        CancellationToken ct = default
    ) => LookUpAsync(order, Asker.Judge, ct);

    /// <summary>The lookup that ends the evidence run: made when none is kept, never a retry.</summary>
    public Task<IReadOnlyList<SiteLookup>> LookUpForEvidenceAsync(
        WorkOrder order,
        CancellationToken ct = default
    ) => LookUpAsync(order, Asker.Evidence, ct);

    private async Task<IReadOnlyList<SiteLookup>> LookUpAsync(
        WorkOrder order,
        Asker asker,
        CancellationToken ct
    )
    {
        var cached = CachedFor(order);

        if (cached is not null && !Retries(order, asker, cached))
            return cached;

        var fresh = await FetchAllAsync(order, ct);
        var lookups = cached is null
            ? fresh
            :
            [
                .. cached.Select(old =>
                    MayBeRetried(old)
                        ? fresh.FirstOrDefault(found => found.Site == old.Site) ?? old
                        : old
                ),
            ];

        SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.WriteFile(
            Path.Combine(workingSet.Prepare(order.TrackKey), SourcesFileName),
            lookups
        );

        return lookups;
    }

    /// <summary>Whether a kept lookup has a site to try again, which each track gets once per start for the rule pass and once for the judge.</summary>
    private bool Retries(WorkOrder order, Asker asker, IReadOnlyList<SiteLookup> cached) =>
        asker is not Asker.Evidence
        && cached.Any(MayBeRetried)
        && TakeRetry($"{order.TrackKey}/{asker}");

    private bool TakeRetry(string key)
    {
        lock (_retried)
            return _retried.Add(key);
    }

    private static bool MayBeRetried(SiteLookup lookup) =>
        lookup.Status is LookupStatus.Unreachable or LookupStatus.NotLookedUp;

    private async Task<IReadOnlyList<SiteLookup>> FetchAllAsync(
        WorkOrder order,
        CancellationToken ct
    ) =>
        NamesOf(order) is var (artist, title)
            ? await pages.FetchAsync(
                artist,
                title,
                AlsoCreditedOf(order),
                IntakeOf(order)?.Release?.Album,
                ct
            )
            :
            [
                .. SiteNames.Select(site => new SiteLookup(site, LookupStatus.NotLookedUp, [])
                {
                    Error = "the track names no artist and title to look up",
                }),
            ];

    private enum Asker
    {
        Evidence,
        Rules,
        Judge,
    }

    /// <summary>The pages as the rules and the judge are to read them: the lookup's, with the person's choices applied (ADR 0078).</summary>
    public IReadOnlyList<SiteLookup> Effective(string trackKey, IReadOnlyList<SiteLookup> found) =>
        choices is null ? found : PageChoiceEffect.Apply(found, choices.ActiveFor(trackKey));

    /// <summary>What an earlier lookup kept for this track, or null when there is none.</summary>
    public IReadOnlyList<SiteLookup>? CachedFor(WorkOrder order) => CachedFor(order.TrackKey);

    public IReadOnlyList<SiteLookup>? CachedFor(string trackKey) =>
        SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.ReadFileOrDefault(
            Path.Combine(
                Path.GetDirectoryName(workingSet.EvidencePathFor(trackKey))!,
                SourcesFileName
            )
        );

    /// <summary>The ledger's names for a song judged before, else the row's <c>Artist - Title</c>, else StemForge's name for a title that names no artist (a <c>Topic</c> upload's).</summary>
    public (string Artist, string Title)? NamesOf(WorkOrder order) =>
        HeldRun(order) is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? (artist, title)
            : TaggedNamesOf(order)
                ?? Split(order.Title)
                ?? (
                    names?.Read().GetValueOrDefault(order.TrackKey)
                        is { DisplayTitle: var resolved }
                        ? Split(resolved)
                        : null
                );

    /// <summary>The album artist, composer and organization a file credits: names a page may be filed under (ADR 0077).</summary>
    public IReadOnlyList<string> AlsoCreditedOf(WorkOrder order) =>
        IntakeOf(order)?.Release is { } release
            ?
            [
                .. new[]
                {
                    release.AlbumArtist,
                    release.Composer,
                    release.Organization,
                }.OfType<string>(),
            ]
            : [];

    /// <summary>What a file's own tags call the track; a file with no title tag is its name alone, since a split would cut it at a dash in the title.</summary>
    private (string Artist, string Title)? TaggedNamesOf(WorkOrder order) =>
        IntakeOf(order) switch
        {
            { Release: not null, SongTitle: { Length: > 0 } songTitle } record => (
                record.Artist ?? string.Empty,
                songTitle
            ),
            { Release: not null } record => (string.Empty, record.Title),
            _ => null,
        };

    private IntakeRecord? IntakeOf(WorkOrder order) =>
        intake
            ?.Read()
            .FirstOrDefault(record =>
                record.TrackKey.Equals(order.TrackKey, StringComparison.Ordinal)
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

        return TrackKeys.TryParse(order.SourceUrl, out var source)
            && ledger.TryGetLatestByTrackKey(source, out var swapped)
                ? swapped
            : ledger.TryGetLatestByTrackKey(order.TrackKey, out var held) ? held
            : null;
    }
}
