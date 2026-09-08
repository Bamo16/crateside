using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Produces the evidence for one track: acquires the audio, then runs the detectors over it.
/// </summary>
/// <remarks>
/// The downloader must be <c>stemforge-cli download</c>: it writes provenance into the file's
/// <c>COMMENT</c> tag, which <c>separate</c> reads back to carry the source URL, and therefore
/// the video ID, into every stem. A file fetched any other way would separate into stems that
/// join to nothing.
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

        // Checked against the file, not the exit code — a clean exit that wrote nothing has
        // not produced evidence.
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
