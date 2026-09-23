namespace Crateside.Core.Orchestration;

/// <summary>
/// The wake signal for every stage, so a change can reach the stages it concerns.
/// </summary>
/// <remarks>
/// Separation and Verdict are handed their own <see cref="DrainControl"/> rather than a fresh
/// signal: a wake has to reach the object that worker is already waiting on (ADR 0024).
/// </remarks>
public sealed class StageSignals(DrainControl separation, VerdictControl verdict)
{
    private readonly Dictionary<JobStage, DrainSignal> _signals = new()
    {
        [JobStage.Separation] = separation,
        [JobStage.Verdict] = verdict,
        [JobStage.Download] = new(),
        [JobStage.Evidence] = new(),
        [JobStage.Handoff] = new(),
        [JobStage.Tagging] = new(),
    };

    public DrainSignal For(JobStage stage) => _signals[stage];

    public void Wake(params ReadOnlySpan<JobStage> stages)
    {
        foreach (var stage in stages)
            _signals[stage].Wake();
    }

    /// <summary>
    /// Wakes every stage but the one that just ran, which has its own busy delay already.
    /// </summary>
    /// <remarks>
    /// Broadcast rather than a successor map because the pipeline is not a line: a plan that keeps
    /// the source has Download write straight into what Handoff picks up, skipping four stages.
    /// </remarks>
    public void WakeAllBut(JobStage stage)
    {
        foreach (var (each, signal) in _signals)
            if (each != stage)
                signal.Wake();
    }
}
