using System.Diagnostics;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>Detects over one stretch of a track, for a judge that asked for windows (ADR 0055).</summary>
public interface IWindowDetector
{
    Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    );
}

/// <summary>
/// Produces the evidence for one track: runs the detectors over audio crateside already holds
/// (ADR 0019).
/// </summary>
public sealed class EvidenceRunner(WorkingSet workingSet, DetectorSet detectors) : IWindowDetector
{
    public async Task<EvidenceResult> RunAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (workingSet.HasEvidence(order.VideoId))
            return EvidenceResult.Succeeded(workingSet.EvidencePathFor(order.VideoId));

        if (!workingSet.TryGetSource(order.VideoId, out var audio))
            return EvidenceResult.Failed("no audio in the working set to detect over");

        return await DetectAsync(
            audio,
            window: null,
            workingSet.EvidencePathFor(order.VideoId),
            ct
        );
    }

    public Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    ) => DetectAsync(audioPath, window, evidencePath, ct);

    private async Task<EvidenceResult> DetectAsync(
        string audioPath,
        RunWindow? window,
        string evidencePath,
        CancellationToken ct
    ) =>
        await detectors.DetectAsync(audioPath, window, evidencePath, ct) switch
        {
            DetectionOutcome.Written(var path) => EvidenceResult.Succeeded(path),
            DetectionOutcome.Failed(var error) => EvidenceResult.Failed(error),
            _ => throw new UnreachableException(),
        };
}

/// <summary>How one evidence run turned out.</summary>
public sealed record EvidenceResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>Where the machine layer landed. Null unless it succeeded.</summary>
    public string? EvidencePath { get; init; }

    public string? Error { get; init; }

    public static EvidenceResult Succeeded(string evidencePath) =>
        new() { IsSuccess = true, EvidencePath = evidencePath };

    public static EvidenceResult Failed(string error) => new() { IsSuccess = false, Error = error };
}
