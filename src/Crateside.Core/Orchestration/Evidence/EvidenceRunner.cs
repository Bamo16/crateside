using System.Globalization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Loop;

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
public sealed class EvidenceRunner(WorkingSet workingSet, CratesidePaths paths, string uv = "uv")
    : IWindowDetector
{
    public async Task<EvidenceResult> RunAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (workingSet.HasEvidence(order.VideoId))
            return EvidenceResult.Succeeded(workingSet.EvidencePathFor(order.VideoId));

        if (!workingSet.TryGetSource(order.VideoId, out var audio))
            return EvidenceResult.Failed("no audio in the working set to detect over");

        return await DetectAsync(audio, workingSet.EvidencePathFor(order.VideoId), [], ct);
    }

    public Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    ) =>
        DetectAsync(
            audioPath,
            evidencePath,
            [
                "--segment",
                string.Create(CultureInfo.InvariantCulture, $"{window.Start},{window.End}"),
            ],
            ct
        );

    /// <summary>Runs keytag's declared detector over the audio, writing the machine layer where asked.</summary>
    private async Task<EvidenceResult> DetectAsync(
        string audioPath,
        string evidencePath,
        IEnumerable<string> segment,
        CancellationToken ct
    )
    {
        var outcome = await ExternalProcess.RunAsync(
            uv,
            [
                "run",
                "--project",
                paths.KeytagRepo,
                "keytag-detect",
                audioPath,
                .. segment,
                "--json-out",
                evidencePath,
            ],
            workingDirectory: null,
            ct
        );

        if (!outcome.Succeeded)
            return EvidenceResult.Failed(
                ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"keytag-detect exited with {outcome.ExitCode}"
            );

        // Checked against the file, not the exit code — a clean exit that wrote nothing has
        // not produced evidence.
        return File.Exists(evidencePath)
            ? EvidenceResult.Succeeded(evidencePath)
            : EvidenceResult.Failed("keytag-detect exited cleanly but wrote no evidence");
    }
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
