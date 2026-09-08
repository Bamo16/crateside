namespace Crateside.Core.Orchestration;

/// <summary>Configuration for the drain worker.</summary>
public sealed class DrainOptions
{
    public const string SectionName = "Drain";

    /// <summary>
    /// Whether the app comes up with draining paused. Defaults to true.
    /// </summary>
    /// <remarks>
    /// Do not set <c>false</c> in the base <c>appsettings.json</c> — it loads in every
    /// environment, so that would turn this default on everywhere. Draining is turned on by an
    /// explicit, environment-specific choice.
    /// </remarks>
    public bool StartPaused { get; set; } = true;
}
