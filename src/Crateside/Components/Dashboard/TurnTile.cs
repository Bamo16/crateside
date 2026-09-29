namespace Crateside.Components.Dashboard;

/// <summary>One count on the dashboard that is the user's to act on, and where acting on it happens.</summary>
public sealed record TurnTile(string Label, int Count, string Gist, string Target, string Href)
{
    /// <summary>The count as the folded summary says it: <c>7 in staging</c>.</summary>
    public string Short { get; init; } = Label.ToLowerInvariant();
}
