namespace Crateside.Core.Orchestration.Verdict;

public static class PageStatements
{
    /// <summary>
    /// The keys a page states, as a source's statement: a section that changes key inside itself
    /// reads <c>Outro: F# dorian → F# mixolydian</c> instead of its first key alone.
    /// </summary>
    public static string Of(FetchedPage page, IEnumerable<string> keys) =>
        string.Join(
            "; ",
            keys.Distinct()
                .SelectMany(key =>
                {
                    var sections = page.Keys.Where(pageKey => pageKey.Key == key).ToList();
                    var changes = sections
                        .Select(pageKey => Change(page, pageKey.Section))
                        .OfType<string>()
                        .Distinct()
                        .ToList();

                    return changes.Count is 0 || sections.Count > changes.Count
                        ? changes.Prepend(key)
                        : changes;
                })
                .Distinct()
        );

    private static string? Change(FetchedPage page, string? section) =>
        page.Tab?.Sections.FirstOrDefault(tab =>
            string.Equals(tab.Name, section, StringComparison.OrdinalIgnoreCase)
        )
            is { Keys: [_, _, ..] keys } tab
        && keys.Select(key => key.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
            ? $"{tab.Name}: {string.Join(" → ", keys.OrderBy(key => key.Beat).Select(key => key.Key))}"
            : null;
}
