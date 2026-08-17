using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Produces the <strong>evidence</strong> for one track: acquires the audio, then runs the
/// detectors over it.
/// </summary>
/// <remarks>
/// <para>
/// The stage that contends for nothing — no GPU, no Claude quota, ~15–40s of CPU and a
/// download. That is the whole reason it is a stage of its own, and why it runs unattended
/// rather than inside an agent session: a headless run here would spend quota on work that
/// needs no judgement, which is the split ADR 0001 exists to make.
/// </para>
/// <para>
/// <strong>The downloader is not interchangeable.</strong> It must be
/// <c>stemforge-cli download</c>, because StemForge writes its provenance into the file's
/// <c>COMMENT</c> and reads it back when that file is later fed to <c>separate</c> — which is
/// what carries the source URL, and therefore the video ID, into every stem. The video ID is
/// the join key for the staging board, for "fully processed", and for queue cleanup. A file
/// fetched any other way would separate into stems that join to nothing (ADR 0005).
/// </para>
/// </remarks>
public sealed class EvidenceRunner(
    WorkingSet workingSet,
    CratesidePaths paths,
    string stemforge = "stemforge-cli",
    string uv = "uv"
)
{
    /// <summary>Detects one track, reusing anything its working set already holds.</summary>
    public async Task<EvidenceResult> RunAsync(
        WorkOrder order,
        CancellationToken cancellationToken = default
    )
    {
        // Evidence already on disk is evidence: re-detecting would burn the CPU to reach the
        // same answer, and the stage's own selector treats its presence as "done" anyway.
        if (workingSet.HasEvidence(order.VideoId))
            return EvidenceResult.Succeeded(workingSet.EvidencePathFor(order.VideoId));

        var acquired = await AcquireAsync(order, cancellationToken);

        if (acquired.Error is { } error)
            return EvidenceResult.Failed(error);

        return await DetectAsync(order, acquired.Path!, cancellationToken);
    }

    /// <summary>
    /// The retained download, fetching one first if there is none. Reusing it is what makes the
    /// whole pipeline cost a single fetch per track.
    /// </summary>
    private async Task<(string? Path, string? Error)> AcquireAsync(
        WorkOrder order,
        CancellationToken cancellationToken
    )
    {
        if (workingSet.TryGetSource(order.VideoId, out var retained))
            return (retained, null);

        var outcome = await ExternalProcess.RunAsync(
            stemforge,
            [
                "download",
                order.SourceUrl,
                "--json",
                "--format",
                order.Recipe.Format,
                "--output",
                workingSet.Prepare(order.VideoId),
            ],
            workingDirectory: null,
            cancellationToken
        );

        if (!outcome.Succeeded)
            return (
                null,
                ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"stemforge-cli download exited with {outcome.ExitCode}"
            );

        // A zero exit still has to be checked: the CLI reports per-input failures inside its
        // JSON rather than by failing the whole process.
        return Parse(outcome.StandardOutput) switch
        {
            null => (null, "stemforge-cli download reported success but produced no readable JSON"),
            [{ Succeeded: true, Path: { Length: > 0 } path }, ..] => (path, null),
            [{ Error: { Length: > 0 } error }, ..] => (null, error),
            _ => (null, "stemforge-cli download failed without saying why"),
        };
    }

    /// <summary>
    /// Runs keytag's declared detector over the audio, writing the machine layer into the
    /// working set for a verdict run to pick up later.
    /// </summary>
    /// <remarks>
    /// <c>--project</c> rather than a working-directory change, so the absolute paths passed in
    /// still resolve against crateside. Nothing here reaches past the declared name.
    /// </remarks>
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

        // Checking reality rather than the exit code, the same idiom the verdict stage uses:
        // the artifact is the point, and a detector that exited clean without writing one has
        // not produced evidence whatever it claims.
        return File.Exists(evidencePath)
            ? EvidenceResult.Succeeded(evidencePath)
            : EvidenceResult.Failed("keytag-detect exited cleanly but wrote no evidence");
    }

    private static IReadOnlyList<DownloadOutput>? Parse(string stdout)
    {
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<DownloadOutput>>(stdout[start..]);
        }
        catch (JsonException)
        {
            return null;
        }
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

/// <summary>One element of <c>stemforge-cli download</c>'s <c>--json</c> array.</summary>
internal sealed record DownloadOutput
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
