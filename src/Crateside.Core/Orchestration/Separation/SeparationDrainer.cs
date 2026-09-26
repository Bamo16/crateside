using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Separation;

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
    public async Task<DrainTick> DrainOnceAsync(CancellationToken ct = default)
    {
        if (control.IsPaused)
            return DrainTick.Paused;

        QueueView view;

        try
        {
            view = await queue.ReadAsync(ct);
        }
        catch (QueueUnavailableException ex)
        {
            return DrainTick.Unavailable(ex.Message);
        }

        if (!selector.TryPickNext(view, KeyLedger.Read(_paths.Ledger), out var order))
            return DrainTick.Idle;

        control.Report($"Separating {order.Title}");

        try
        {
            var result = await runner.RunAsync(order, ct);

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

        return Moved(paths, outputs);
    }

    /// <summary>
    /// Moves every parked file whose song has since been judged well enough to tag, whatever parked
    /// it, and recycles one already staged byte for byte; returns how many went.
    /// </summary>
    public static int ReleaseParked(
        CratesidePaths paths,
        KeyLedger ledger,
        JobStore jobs,
        DeletionLog? deletions = null
    )
    {
        if (!Directory.Exists(paths.StemsUnresolvedKey))
            return 0;

        var released = Directory
            .EnumerateFiles(paths.StemsUnresolvedKey)
            .Where(SampleFiles.IsAudio)
            .Select(StemTagReader.Read)
            .OfType<StemTags>()
            .Where(tags =>
                tags.SourceVideoId is { } videoId
                && ledger.TryGetLatestByVideoId(videoId, out var run)
                && run.HasTaggableKey
                // A separation still writing here moves its own outputs when it lands.
                && !(jobs.TryGet(videoId, JobStage.Separation, out var job) && job.IsOpen)
            )
            .Select(tags => tags.Path)
            .ToArray();

        var duplicates = released.Where(path => IsStagedAlready(paths, path)).ToArray();

        foreach (var duplicate in duplicates)
            (deletions ?? DeletionLog.None).Recycle(duplicate, "a copy already in Sample Staging");

        // A staged file of the same name and different bytes stays parked: the handoff would only
        // refuse it, and which one is right is the user's call.
        var clear = released
            .Except(duplicates)
            .Where(path => !File.Exists(Path.Combine(paths.SampleStaging, Path.GetFileName(path))));

        return duplicates.Length + Moved(paths, clear).Count;
    }

    private static bool IsStagedAlready(CratesidePaths paths, string parked)
    {
        var staged = Path.Combine(paths.SampleStaging, Path.GetFileName(parked));

        if (!File.Exists(staged) || new FileInfo(staged).Length != new FileInfo(parked).Length)
            return false;

        using var a = File.OpenRead(staged);
        using var b = File.OpenRead(parked);

        return System
            .Security.Cryptography.SHA256.HashData(a)
            .AsSpan()
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(b));
    }

    private static IReadOnlyList<string> Moved(CratesidePaths paths, IEnumerable<string> files)
    {
        var destination = paths.DestinationFor(StemDestination.KeyIdentified);
        List<string> moved = [];

        Directory.CreateDirectory(destination);

        foreach (var file in files.Where(File.Exists))
        {
            var landing = Path.Combine(destination, Path.GetFileName(file));

            try
            {
                File.Move(file, landing, overwrite: true);
                moved.Add(landing);

                if (File.Exists(StemFileName.AnalysisFor(file)))
                    File.Move(
                        StemFileName.AnalysisFor(file),
                        StemFileName.AnalysisFor(landing),
                        overwrite: true
                    );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
