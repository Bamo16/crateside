using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works the queue down, one track at a time, for as long as there is anything outstanding.
/// </summary>
public sealed class SeparationDrainer(
    QueueService queue,
    SeparationRunner runner,
    SeparationSelector selector,
    DrainControl control,
    IOptions<CratesidePaths> paths
)
{
    private readonly CratesidePaths _paths = paths.Value;

    /// <summary>Separates the next outstanding track, if there is one and nothing is in the way.</summary>
    public async Task<DrainTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        if (control.IsPaused)
            return DrainTick.Paused;

        QueueView view;

        try
        {
            view = await queue.ReadAsync(cancellationToken);
        }
        catch (QueueUnavailableException e)
        {
            return DrainTick.Unavailable(e.Message);
        }

        if (!selector.TryPickNext(view, KeyLedger.Read(_paths.Ledger), out var order))
            return DrainTick.Idle;

        control.Report($"Separating {order.Title}");

        try
        {
            return DrainTick.Ran(order, await runner.RunAsync(order, cancellationToken));
        }
        finally
        {
            control.Report(null);
        }
    }
}

/// <summary>What one pass of the drainer did.</summary>
public sealed record DrainTick
{
    public static DrainTick Paused { get; } = new() { Outcome = DrainOutcome.Paused };

    public static DrainTick Idle { get; } = new() { Outcome = DrainOutcome.Idle };

    public required DrainOutcome Outcome { get; init; }

    public WorkOrder? Order { get; init; }
    public SeparationResult? Result { get; init; }
    public string? Error { get; init; }

    public static DrainTick Ran(WorkOrder order, SeparationResult result) =>
        new()
        {
            Outcome = DrainOutcome.Ran,
            Order = order,
            Result = result,
        };

    public static DrainTick Unavailable(string error) =>
        new() { Outcome = DrainOutcome.Unavailable, Error = error };
}

public enum DrainOutcome
{
    Ran,
    Idle,
    Paused,

    /// <summary>The playlist could not be read, distinct from an empty queue.</summary>
    Unavailable,
}
