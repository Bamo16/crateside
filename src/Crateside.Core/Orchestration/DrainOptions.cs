namespace Crateside.Core.Orchestration;

/// <summary>Configuration for the drain worker.</summary>
public sealed class DrainOptions
{
    public const string SectionName = "Drain";

    /// <summary>
    /// Whether the app comes up with draining paused. <strong>Defaults to true.</strong>
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a decision about the <em>fallback</em>, not about normal behaviour: ADR 0001 is
    /// unchanged, and a configured deployment turns draining on. But the value you get when
    /// configuration does not load must not be the one that consumes a scarce resource.
    /// </para>
    /// <para>
    /// It defaulted to false, and on 2026-08-16 the app was launched with a malformed
    /// <c>--no-launch-profile=false</c>. That left <c>ASPNETCORE_ENVIRONMENT</c> unset, so
    /// <c>appsettings.Development.json</c> — which held the pause — never loaded, and the
    /// drainer separated a track about a second after startup. Nothing was broken; the default
    /// simply pointed the wrong way, and every layer above it did its job.
    /// </para>
    /// <para>
    /// For the same reason there is no <c>StartPaused</c> in the base <c>appsettings.json</c>:
    /// putting <c>false</c> there would reintroduce exactly this, since the base file loads in
    /// every environment. Draining is turned on by an explicit, environment-specific choice.
    /// </para>
    /// </remarks>
    public bool StartPaused { get; set; } = true;
}
