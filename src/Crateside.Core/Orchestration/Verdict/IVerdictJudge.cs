namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Rules on the keys crateside's own rules leave open, by whatever means: today a headless Claude Code session (ADR 0076).</summary>
public interface IVerdictJudge
{
    /// <summary>Judges one track: records its verdict, or says why there is none.</summary>
    Task<VerdictResult> JudgeAsync(
        WorkOrder order,
        JudgeDirection? direction,
        Action<JudgeStep> onStep,
        CancellationToken ct = default
    );
}
