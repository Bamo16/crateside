using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// The wake signal for every stage — for Separation and Verdict, the control each already waits
/// on (ADR 0024).
/// </summary>
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
    /// Wakes every stage but the one that just ran, since the pipeline is not a line and any
    /// stage may be next.
    /// </summary>
    public void WakeAllBut(JobStage stage)
    {
        foreach (var (each, signal) in _signals)
            if (each != stage)
                signal.Wake();
    }
}
