namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a lookup's pages come to once the person's choices are applied (ADR 0078).</summary>
internal static class PageChoiceEffect
{
    /// <summary>The lookup's pages minus the rejected ones, plus the promoted and added ones; the lookups themselves are not changed.</summary>
    public static IReadOnlyList<SiteLookup> Apply(
        IReadOnlyList<SiteLookup> lookups,
        IReadOnlyList<PageChoice> choices
    )
    {
        if (choices is [])
            return lookups;

        var sites = lookups
            .Select(lookup => lookup.Site)
            .Concat(choices.Select(choice => choice.Page.Site))
            .Distinct()
            .ToList();

        return
        [
            .. sites.Select(site =>
                Apply(
                    lookups.FirstOrDefault(lookup => lookup.Site == site)
                        ?? new SiteLookup(site, LookupStatus.NotFound, []),
                    [.. choices.Where(choice => choice.Page.Site == site)]
                )
            ),
        ];
    }

    private static SiteLookup Apply(SiteLookup lookup, List<PageChoice> choices)
    {
        if (choices is [])
            return lookup;

        bool Chosen(string url, PageChoiceKind kind) =>
            choices.Any(choice => choice.Kind == kind && PageChoiceStore.SameUrl(choice.Url, url));

        var rejected = choices.Where(choice => choice.Kind is PageChoiceKind.Rejected).ToList();
        var counted = choices.Where(choice => choice.Kind is not PageChoiceKind.Rejected).ToList();
        var found = lookup.Pages.Where(page => !Chosen(page.Url, PageChoiceKind.Rejected)).ToList();
        var pages = found
            .Concat(
                counted
                    .Where(choice =>
                        !found.Any(page => PageChoiceStore.SameUrl(page.Url, choice.Url))
                    )
                    .Select(choice => choice.Page with { Chosen = choice.Kind })
            )
            .ToList();
        var candidates = (lookup.Candidates ?? [])
            .Where(page => !counted.Any(choice => PageChoiceStore.SameUrl(choice.Url, page.Url)))
            .ToList();

        return lookup with
        {
            Status = (lookup.Status, pages) switch
            {
                (LookupStatus.NotFound, [_, ..]) => LookupStatus.Found,
                (LookupStatus.Found, []) => LookupStatus.NotFound,
                (var status, _) => status,
            },
            Pages = pages,
            Candidates = candidates is [] ? null : candidates,
            Rejected = rejected is [] ? null : [.. rejected.Select(choice => choice.Page)],
        };
    }
}
