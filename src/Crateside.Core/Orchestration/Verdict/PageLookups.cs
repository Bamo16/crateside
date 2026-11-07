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

        var lookups = await pages.FetchAsync(artist, title, AlsoCreditedOf(order), ct);

        if (!lookups.Any(VerdictRules.MayHoldAnotherKey))
            SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.WriteFile(
                Path.Combine(workingSet.Prepare(order.TrackKey), SourcesFileName),
                lookups
            );

        return lookups;
    }

    /// <summary>The pages as the rules and the judge are to read them: the lookup's, with the person's choices applied (ADR 0078).</summary>
    public IReadOnlyList<SiteLookup> Effective(string trackKey, IReadOnlyList<SiteLookup> found) =>
        choices is null ? found : PageChoiceEffect.Apply(found, choices.ActiveFor(trackKey));

    /// <summary>What an earlier lookup kept for this track, or null when there is none.</summary>
    public IReadOnlyList<SiteLookup>? CachedFor(WorkOrder order) =>
        SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.ReadFileOrDefault(
            Path.Combine(
                Path.GetDirectoryName(workingSet.EvidencePathFor(order.TrackKey))!,
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
