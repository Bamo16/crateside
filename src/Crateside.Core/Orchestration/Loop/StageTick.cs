using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>A stage that works its queue down one track at a time. Separation is not one.</summary>
public interface IStageDrainer
{
    JobStage Stage { get; }

    Task<StageTick> DrainOnceAsync(CancellationToken ct = default);
}

/// <summary>What one pass of a stage did. Separation reports <see cref="DrainTick"/> instead.</summary>
public sealed record StageTick
{
    public static StageTick Idle { get; } = new() { Outcome = StageOutcome.Idle };

    public static StageTick Paused { get; } = new() { Outcome = StageOutcome.Paused };

    public static StageTick Held(string reason) =>
        new() { Outcome = StageOutcome.Held, Error = reason };

    public required StageOutcome Outcome { get; init; }

    /// <summary>
    /// The order this pass worked. Null for <see cref="JobStage.Handoff"/>, which reads no queue —
    /// use <see cref="Subject"/>, which every stage has.
    /// </summary>
    public WorkOrder? Order { get; init; }

    public string? Subject { get; init; }

    public bool IsSuccess { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Something a successful pass did that the user would want to know about — a deletion, say.
    /// Not an error, and not every pass has one.
    /// </summary>
    public string? Note { get; init; }

    public static StageTick Ran(WorkOrder order, bool isSuccess, string? error) =>
        new()
        {
            Outcome = StageOutcome.Ran,
            Order = order,
            Subject = order.Title,
            IsSuccess = isSuccess,
            Error = error,
        };

    /// <summary>For a stage whose work is not described by a <see cref="WorkOrder"/>.</summary>
    public static StageTick Ran(
        string subject,
        bool isSuccess,
        string? error,
        string? note = null
    ) =>
        new()
        {
            Outcome = StageOutcome.Ran,
            Subject = subject,
            IsSuccess = isSuccess,
            Error = error,
            Note = note,
        };

    public static StageTick Unavailable(string error) =>
        new() { Outcome = StageOutcome.Unavailable, Error = error };
}

public enum StageOutcome
{
    Ran,
    Idle,
    Paused,

    /// <summary>Held back by a guard that lifts by itself, not paused by the user.</summary>
    Held,

    /// <summary>The playlist could not be read. Distinct from an empty queue, deliberately.</summary>
    Unavailable,
}
