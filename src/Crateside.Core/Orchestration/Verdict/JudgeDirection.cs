using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What the user asks of a judgment from these bars: the windows, a note, and what they hear (ADR 0055).</summary>
/// <param name="Audio">The full mix to detect over; null when it has to be downloaded again first.</param>
public sealed record JudgeDirection(
    IReadOnlyList<RunWindow> Windows,
    string? Note,
    TonalCentre? Ear,
    string? Audio
);

/// <summary>Where a judgment from these bars has got to.</summary>
public abstract record JudgeStep
{
    private JudgeStep() { }

    /// <summary>The full mix, downloaded again because crateside no longer held it; never separated.</summary>
    public sealed record Downloading : JudgeStep;

    /// <summary>The whole track, when crateside holds no evidence for it any more.</summary>
    public sealed record DetectingWhole : JudgeStep;

    /// <param name="Number">Counted from 1.</param>
    public sealed record DetectingWindow(int Number, int Of, RunWindow Window) : JudgeStep;

    public sealed record Judging : JudgeStep;

    /// <summary>The judge ended without a usable verdict and is asked once more.</summary>
    public sealed record Resuming(string Why) : JudgeStep;

    /// <summary>The judge withheld and asked for windows of its own, which crateside runs unattended.</summary>
    public sealed record JudgeAsked(IReadOnlyList<RunWindow> Windows) : JudgeStep;
}
