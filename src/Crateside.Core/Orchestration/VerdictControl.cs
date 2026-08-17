namespace Crateside.Core.Orchestration;

/// <summary>
/// Whether the verdict stage is running, and what it is doing right now.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Its own control rather than a flag on the separation one</strong>, because the two
/// stop for unrelated reasons at unrelated times: separation for whatever makes the user want
/// the GPU left alone, judgment for a working session where quota is wanted elsewhere. Sharing
/// one toggle would mean stopping the GPU to save quota, or spending quota to free the GPU.
/// </para>
/// <para>
/// <strong>This reverses part of ADR 0005's decision 8</strong>, which held that a verdict run
/// was cheap enough not to earn a control. That rested on a comparison rather than a
/// measurement, and the measurement — taken 2026-08-17, once the headless invocation actually
/// ran — is ~37k tokens of session start-up <em>before any evidence is weighed</em>, every
/// track. A queue drains once, so the total is finite; but it is spent unattended and in one
/// burst, and the reason to stop it is exactly the reason ADR 0001 gave for scheduling this
/// stage separately in the first place: quota is tight during working sessions and idle
/// otherwise.
/// </para>
/// <para>
/// It starts paused for the same reason separation does — launching the app should never itself
/// be the act that spends something.
/// </para>
/// </remarks>
public sealed class VerdictControl(bool startPaused = true) : DrainControl(startPaused);
