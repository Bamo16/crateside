using System.Diagnostics;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>Detects over one stretch of a track, for windows the judge or the user asked for (ADR 0055).</summary>
public interface IWindowDetector
{
    Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    );

    /// <summary>The whole track, for a run whose working set no longer holds its evidence.</summary>
    Task<EvidenceResult> DetectWholeAsync(
        string audioPath,
        string evidencePath,
        CancellationToken ct = default
    );
}

/// <summary>
/// Produces the evidence for one track: runs the detectors over audio crateside already holds
/// (ADR 0019).
/// </summary>
public sealed class EvidenceRunner(WorkingSet workingSet, IDetectorSet detectors) : IWindowDetector
{
    public async Task<EvidenceResult> RunAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (workingSet.HasEvidence(order.TrackKey))
            return EvidenceResult.Succeeded(
                workingSet.EvidencePathFor(order.TrackKey),
                "evidence was already on file"
            );

        if (!workingSet.TryGetSource(order.TrackKey, out var audio))
            return EvidenceResult.Failed("no audio in the working set to detect over");

        return await DetectAsync(
            audio,
            window: null,
            workingSet.EvidencePathFor(order.TrackKey),
            ct
        );
    }

    public Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    ) => DetectAsync(audioPath, window, evidencePath, ct);

    public Task<EvidenceResult> DetectWholeAsync(
        string audioPath,
        string evidencePath,
        CancellationToken ct = default
    ) => DetectAsync(audioPath, window: null, evidencePath, ct);

    private async Task<EvidenceResult> DetectAsync(
        string audioPath,
        RunWindow? window,
        string evidencePath,
        CancellationToken ct
    ) =>
        await detectors.DetectAsync(audioPath, window, evidencePath, ct) switch
        {
            DetectionOutcome.Written(var path) written => EvidenceResult.Succeeded(
                path,
                written.Summary
            ),
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

    /// <summary>What the detectors read, in a line for the activity feed (#170). Null unless it succeeded.</summary>
    public string? Summary { get; init; }

    public static EvidenceResult Succeeded(string evidencePath, string? summary = null) =>
        new()
        {
            IsSuccess = true,
            EvidencePath = evidencePath,
            Summary = summary,
        };

    public static EvidenceResult Failed(string error) => new() { IsSuccess = false, Error = error };
}
