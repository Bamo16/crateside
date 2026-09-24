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
            var result = await runner.RunAsync(order, cancellationToken);

            if (result.Outcome is SeparationOutcome.Succeeded)
                StemLanding.Reconcile(
                    _paths,
                    KeyLedger.Read(_paths.Ledger),
                    order,
                    result.OutputFiles
                );

            return DrainTick.Ran(order, result);
        }
        finally
        {
            control.Report(null);
        }
    }
}

/// <summary>Where a finished run's stems belong, once it is finished.</summary>
public static class StemLanding
{
    /// <summary>
    /// Moves a run into the key-identified root when the verdict arrived while it was running,
    /// after its destination was chosen.
    /// </summary>
    public static IReadOnlyList<string> Reconcile(
        CratesidePaths paths,
        KeyLedger ledger,
        WorkOrder order,
        IReadOnlyList<string> outputs
    )
    {
        if (
            order.Destination is not StemDestination.UnresolvedKey
            || !ledger.TryGetLatestByVideoId(order.VideoId, out var run)
            || !run.HasTaggableKey
        )
            return [];

        var destination = paths.DestinationFor(StemDestination.KeyIdentified);
        List<string> moved = [];

        Directory.CreateDirectory(destination);

        foreach (var file in outputs.Where(File.Exists))
        {
            var landing = Path.Combine(destination, Path.GetFileName(file));

            try
            {
                File.Move(file, landing, overwrite: true);
                moved.Add(landing);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left parked rather than lost, which is where it would have stayed anyway.
            }
        }

        return moved;
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
