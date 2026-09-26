using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>
/// Produces the evidence for one track: runs the detectors over audio crateside already holds
/// (ADR 0019).
/// </summary>
public sealed class EvidenceRunner(WorkingSet workingSet, CratesidePaths paths, string uv = "uv")
{
    public async Task<EvidenceResult> RunAsync(
        WorkOrder order,
        CancellationToken cancellationToken = default
    )
    {
        if (workingSet.HasEvidence(order.VideoId))
            return EvidenceResult.Succeeded(workingSet.EvidencePathFor(order.VideoId));

        if (!workingSet.TryGetSource(order.VideoId, out var audio))
            return EvidenceResult.Failed("no audio in the working set to detect over");

        return await DetectAsync(order, audio, cancellationToken);
    }

    /// <summary>
    /// Runs keytag's declared detector over the audio, writing the machine layer into the
    /// working set for a verdict run to pick up later.
    /// </summary>
    private async Task<EvidenceResult> DetectAsync(
        WorkOrder order,
        string audioPath,
        CancellationToken cancellationToken
    )
    {
        var evidencePath = workingSet.EvidencePathFor(order.VideoId);

        var outcome = await ExternalProcess.RunAsync(
            uv,
            [
                "run",
                "--project",
                paths.KeytagRepo,
                "keytag-detect",
                audioPath,
                "--json-out",
                evidencePath,
            ],
            workingDirectory: null,
            cancellationToken
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
