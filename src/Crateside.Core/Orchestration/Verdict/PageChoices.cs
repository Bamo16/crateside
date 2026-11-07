using System.Diagnostics;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a correction to the pages came to for the track's verdict (ADR 0078).</summary>
public abstract record PageChoiceOutcome
{
    private PageChoiceOutcome() { }

    /// <summary>The page was not taken, or the choice could not be made.</summary>
    public sealed record Refused(string Message) : PageChoiceOutcome;

    /// <summary>The rules ruled again on the pages as corrected and recorded a new run.</summary>
    public sealed record Ruled(string Summary) : PageChoiceOutcome;

    /// <summary>The rules ran again and settle nothing; any earlier verdict stands.</summary>
    public sealed record Open : PageChoiceOutcome;

    /// <summary>A judge's or the person's verdict stands, and is not re-judged.</summary>
    public sealed record Kept(DecidedBy By) : PageChoiceOutcome;
}

/// <summary>The person's corrections to the pages that count for a track, and the re-ruling they cause (ADR 0078).</summary>
public sealed class PageChoices(
    PageChoiceStore store,
    ISourcePages pages,
    PageLookups lookups,
    VerdictByRules rules,
    JobStore jobs,
    TimeProvider time
)
{
    /// <summary>The track's pages as the song page shows them: what the lookup found with the current choices applied.</summary>
    public IReadOnlyList<SiteLookup> Shown(string trackKey, IReadOnlyList<SiteLookup> found) =>
        lookups.Effective(trackKey, found);

    /// <summary>Whether a choice for the track was made or undone after <paramref name="since"/>.</summary>
    public bool ChangedSince(string trackKey, DateTimeOffset since) =>
        store.LastChange(trackKey) > since;

    public Task<PageChoiceOutcome> RejectAsync(
        WorkOrder order,
        FetchedPage page,
        CancellationToken ct = default
    ) => ChooseAsync(order, page, PageChoiceKind.Rejected, ct);

    public Task<PageChoiceOutcome> CountAsync(
        WorkOrder order,
        FetchedPage candidate,
        CancellationToken ct = default
    ) => ChooseAsync(order, candidate, PageChoiceKind.Promoted, ct);

    public async Task<PageChoiceOutcome> AddAsync(
        WorkOrder order,
        string address,
        CancellationToken ct = default
    )
    {
        var fetched = await pages.FetchPageAsync(address, ct);

        return fetched switch
        {
            PageFetch.Refused(var message) => new PageChoiceOutcome.Refused(message),
            PageFetch.Found(var page) when AlreadyCounted(order, page) =>
                new PageChoiceOutcome.Refused("That page is already counted."),
            PageFetch.Found(var page) => await ChooseAsync(order, page, PageChoiceKind.Added, ct),
            _ => throw new UnreachableException(),
        };
    }

    public async Task<PageChoiceOutcome> UndoAsync(
        WorkOrder order,
        string url,
        CancellationToken ct = default
    ) =>
        store.Undo(order.TrackKey, url, time.GetUtcNow())
            ? await RuleAgainAsync(order, ct)
            : new PageChoiceOutcome.Refused("That choice was already taken back.");

    private bool AlreadyCounted(WorkOrder order, FetchedPage page) =>
        lookups
            .Effective(order.TrackKey, lookups.CachedFor(order) ?? [])
            .Any(lookup => lookup.Pages.Any(held => PageChoiceStore.SameUrl(held.Url, page.Url)));

    private async Task<PageChoiceOutcome> ChooseAsync(
        WorkOrder order,
        FetchedPage page,
        PageChoiceKind kind,
        CancellationToken ct
    )
    {
        store.Choose(
            order.TrackKey,
            new PageChoice
            {
                Url = page.Url,
                Kind = kind,
                Page = page,
                At = time.GetUtcNow(),
            }
        );

        return await RuleAgainAsync(order, ct);
    }

    private async Task<PageChoiceOutcome> RuleAgainAsync(WorkOrder order, CancellationToken ct)
    {
        if (lookups.HeldRun(order) is { DecidedBy: DecidedBy.Judge or DecidedBy.Person } held)
            return new PageChoiceOutcome.Kept(held.DecidedBy);

        switch (await rules.TryRuleAsync(order, ct))
        {
            case null:
                return new PageChoiceOutcome.Open();

            case { IsSuccess: false, Error: var error }:
                return new PageChoiceOutcome.Refused(error ?? "The ledger refused the ruling.");

            case { Summary: var summary }:
                if (
                    jobs.TryGet(order.TrackKey, JobStage.Verdict, out var job)
                    && job.Status is JobStatus.AwaitingPerson
                )
                    jobs.Record(
                        order.TrackKey,
                        JobStage.Verdict,
                        JobStatus.Succeeded,
                        summary: summary
                    );

                return new PageChoiceOutcome.Ruled(summary ?? string.Empty);
        }
    }
}
