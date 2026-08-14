namespace Crateside.Core.Orchestration;

/// <summary>Configuration for the drain worker.</summary>
public sealed class DrainOptions
{
    public const string SectionName = "Drain";

    /// <summary>
    /// Whether the app comes up with draining paused. Default false — draining is the normal
    /// state (ADR 0001), and what limits separation is that two at once are ruinous, not that
    /// one costs the user their machine.
    /// </summary>
    /// <remarks>
    /// Set true while separation would conflict with something else. <strong>It currently is,
    /// in this repo's Development settings:</strong> <c>detect-key</c> still separates as the
    /// default second half of every run, so a track crateside separated would be separated
    /// again by the skill — duplicate stems and twice the GPU. Flip this back once the skill
    /// stops separating.
    /// </remarks>
    public bool StartPaused { get; set; }
}
