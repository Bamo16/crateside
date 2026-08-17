namespace Crateside.Core.Orchestration;

/// <summary>
/// A stage that works its queue down one track at a time.
/// </summary>
/// <remarks>
/// Exists so one loop can drive several stages. Separation is deliberately not one of these: it
/// is single-flight, adopts orphaned processes on startup, and can refuse because the GPU is
/// busy — none of which the other two have any use for.
/// </remarks>
public interface IStageDrainer
{
    /// <summary>Which stage this is, for the log and the job store.</summary>
    JobStage Stage { get; }

    Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one pass of a stage did.</summary>
/// <remarks>
/// Separation reports its own richer <see cref="DrainTick"/>, which additionally carries the
/// stem files written and the refused-because-the-GPU-is-busy outcome. Neither applies here:
/// detection and judgment produce one artifact each and contend for nothing that could refuse
/// them.
/// </remarks>
public sealed record StageTick
{
    /// <summary>Nothing to do — the resting state once the queue has been worked down.</summary>
    public static StageTick Idle { get; } = new() { Outcome = StageOutcome.Idle };

    /// <summary>Stopped on purpose. Only stages that have a control can report this.</summary>
    public static StageTick Paused { get; } = new() { Outcome = StageOutcome.Paused };

    public required StageOutcome Outcome { get; init; }

    public WorkOrder? Order { get; init; }
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }

    public static StageTick Ran(WorkOrder order, bool isSuccess, string? error) =>
        new()
        {
            Outcome = StageOutcome.Ran,
            Order = order,
            IsSuccess = isSuccess,
            Error = error,
        };

    public static StageTick Unavailable(string error) =>
        new() { Outcome = StageOutcome.Unavailable, Error = error };
}

public enum StageOutcome
{
    Ran,
    Idle,
    Paused,

    /// <summary>The playlist could not be read. Distinct from an empty queue, deliberately.</summary>
    Unavailable,
}
