namespace Crateside.Core.Orchestration;

/// <summary>Whether the verdict stage is running, and what it is doing right now.</summary>
public sealed class VerdictControl(bool startPaused = true) : DrainControl(startPaused);
