namespace Crateside.Core.Orchestration;

/// <summary>Configuration for the drain worker.</summary>
public sealed class DrainOptions
{
    public const string SectionName = "Drain";

    /// <summary>
    /// Whether the app comes up with draining paused. Never set <c>false</c> in the base
    /// <c>appsettings.json</c>, which every environment loads.
    /// </summary>
    public bool StartPaused { get; set; } = true;
}
