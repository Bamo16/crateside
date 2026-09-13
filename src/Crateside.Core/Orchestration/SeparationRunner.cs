using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Humanizer;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Runs one <see cref="WorkOrder"/> through <c>stemforge-cli separate</c>.
/// </summary>
public sealed class SeparationRunner(
    SeparationInterlock interlock,
    WorkingSet workingSet,
    CratesidePaths paths,
    string executable = "stemforge-cli"
)
{
    /// <summary>
    /// Separates one track, start to finish; refuses rather than queues if the GPU is already
    /// busy.
    /// </summary>
    public async Task<SeparationResult> RunAsync(
        WorkOrder order,
        CancellationToken cancellationToken = default
    )
    {
        if (interlock.TryGetLive(out var live))
            return SeparationResult.Refused($"already separating {live.VideoId}");

        var retained = workingSet.TryGetSource(order.VideoId, out var path) ? path : null;

        // Nothing to separate. Reachable once a plan can be added to: a track that already has
        // every preset it asks for and then asks to keep its source owes a copy and no passes.
        // stemforge-cli refuses an invocation with neither a preset nor drums, so the copy is
        // made here and the GPU is never claimed.
        if (order.Recipe.Passes is 0)
            return KeepSource(order, retained) is { Count: > 0 } copied
                ? SeparationResult.Succeeded(copied)
                : SeparationResult.Failed(
                    "the source track is the only thing outstanding and it is no longer on disk"
                );

        using var process = Start(order, retained);

        // The check above and this claim must stay adjacent, and only this method may call
        // Start — there is a gap between them since claiming needs the process to exist,
        // harmless only because one worker drives this loop.
        interlock.Claim(order.VideoId, process.Id, StartTimeOf(process));

        try
        {
            // Both reads start before either is awaited: draining stdout first would block if
            // stderr's buffer fills, wedging the child that holds the GPU for six minutes. Only
            // --json keeping stderr quiet makes this safe today, not a guarantee.
            var stdoutRead = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrRead = process.StandardError.ReadToEndAsync(cancellationToken);

            var stdout = await stdoutRead;
            var stderr = await stderrRead;

            await process.WaitForExitAsync(cancellationToken);

            var error = FailureFrom(process.ExitCode, stdout, stderr);
            IReadOnlyList<string> outputs = [];

            if (error is null)
            {
                outputs = [.. OutputFilesFrom(stdout), .. KeepSource(order, retained)];
                error = ShortfallFrom(order.Recipe, outputs);
            }

            interlock.Release(order.VideoId, error);

            return error is null
                ? SeparationResult.Succeeded(outputs)
                : SeparationResult.Failed(error);
        }
        catch (OperationCanceledException)
        {
            // This token fires on shutdown, not pause — pausing only stops the next track being
            // picked up; an in-flight separation is not reachable from the control and runs to
            // completion. Quitting cleanly discards the work here, while a crash keeps it
            // running (Windows does not kill the child), and the interlock adopts it on the
            // next start.
            Stop(process);
            interlock.Release(order.VideoId, "cancelled");

            throw;
        }
    }

    /// <summary>
    /// Whether the run produced what the recipe asked for, said as an error when it did not.
    /// </summary>
    /// <remarks>
    /// Only a shortfall counts as failure; more outputs than the recipe expected is not treated
    /// as harm.
    /// </remarks>
    private static string? ShortfallFrom(SeparationRecipe recipe, IReadOnlyList<string> outputs) =>
        outputs.Count < recipe.ExpectedOutputs
            ? $"the recipe asked for {"file".ToQuantity(recipe.ExpectedOutputs)} "
                + $"and the run produced {outputs.Count}"
            : null;

    /// <summary>
    /// Puts the source track in the destination itself, for a release whose source <em>is</em>
    /// the sample.
    /// </summary>
    /// <remarks>
    /// <c>--keep-source</c> is ignored by <c>stemforge-cli</c> when the input is a local file
    /// rather than a URL, so this method exists to produce the copy directly.
    /// </remarks>
    private IReadOnlyList<string> KeepSource(WorkOrder order, string? retained)
    {
        if (!order.Recipe.KeepSource || retained is null)
            return [];

        try
        {
            var destination = paths.DestinationFor(order.Destination);

            Directory.CreateDirectory(destination);

            var kept = Path.Combine(destination, Path.GetFileName(retained));

            File.Copy(retained, kept, overwrite: true);

            return [kept];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The stems already succeeded, so this does not fail the run; the copy is retried
            // the next time the track separates.
            return [];
        }
    }

    private static DateTimeOffset StartTimeOf(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception e) when (e is InvalidOperationException or SystemException)
        {
            // A process that exited immediately may refuse to report a start time; the
            // interlock treats an unverifiable PID as ours anyway.
            return default;
        }
    }

    /// <summary>
    /// Reads the outcome out of the run. A non-zero exit is decisive; a zero exit still has to
    /// be checked, because the CLI reports per-input failures inside its JSON rather than by
    /// failing the whole process.
    /// </summary>
    private static string? FailureFrom(int exitCode, string stdout, string stderr)
    {
        if (exitCode != 0)
            return Describe(stderr, stdout) ?? $"stemforge-cli exited with {exitCode}";

        var results = Parse(stdout);

        if (results is null)
            return "stemforge-cli reported success but produced no readable JSON";

        return results.FirstOrDefault(r => !r.Succeeded) is { } failed
            ? failed.Error ?? "separation failed without saying why"
            : null;
    }

    private static string? Describe(params string[] streams) =>
        streams
            .Select(s => s.Trim())
            .FirstOrDefault(s => s is { Length: > 0 })
            ?.Split('\n')
            .LastOrDefault(l => !l.AsSpan().IsWhiteSpace())
            ?.Trim();

    private static IReadOnlyList<string> OutputFilesFrom(string stdout) =>
        [.. Parse(stdout)?.SelectMany(r => r.OutputFiles ?? []) ?? []];

    private static IReadOnlyList<SeparationOutput>? Parse(string stdout)
    {
        // The CLI prints a JSON array; anything it logged before it is not ours to interpret.
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<SeparationOutput>>(stdout[start..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or SystemException)
        {
            // It exited on its own between the check and the kill. Nothing to do.
        }
    }

    private Process Start(WorkOrder order, string? retained)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        info.ArgumentList.Add("separate");

        // Safe only because the file is StemForge's own — it reads back the provenance comment
        // stemforge-cli download wrote, carrying the source URL, and therefore the video ID,
        // into the stems. A file from elsewhere would produce stems that join to nothing.
        info.ArgumentList.Add(retained ?? order.SourceUrl);

        foreach (var preset in order.Recipe.Presets)
        {
            info.ArgumentList.Add("--preset");
            info.ArgumentList.Add(preset);
        }

        if (order.Recipe.ExtractDrums)
            info.ArgumentList.Add("--extract-drums");

        // Only meaningful for a URL input; a local file ignores it, which is why KeepSource
        // exists to cover that case.
        if (order.Recipe.KeepSource && retained is null)
            info.ArgumentList.Add("--keep-source");

        info.ArgumentList.Add("--format");
        info.ArgumentList.Add(order.Recipe.Format);
        info.ArgumentList.Add("--output");
        info.ArgumentList.Add(paths.DestinationFor(order.Destination));
        info.ArgumentList.Add("--json");

        return Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {executable}");
    }
}

/// <summary>How one separation turned out.</summary>
public sealed record SeparationResult
{
    public required SeparationOutcome Outcome { get; init; }

    /// <summary>Absolute stem paths, straight from the CLI. Empty unless it succeeded.</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = [];

    public string? Error { get; init; }

    public static SeparationResult Succeeded(IReadOnlyList<string> outputFiles) =>
        new() { Outcome = SeparationOutcome.Succeeded, OutputFiles = outputFiles };

    public static SeparationResult Failed(string error) =>
        new() { Outcome = SeparationOutcome.Failed, Error = error };

    public static SeparationResult Refused(string reason) =>
        new() { Outcome = SeparationOutcome.Refused, Error = reason };
}

public enum SeparationOutcome
{
    Succeeded,
    Failed,

    /// <summary>Not attempted, because the GPU was already busy. Not a failure of the track.</summary>
    Refused,
}

/// <summary>One element of <c>stemforge-cli</c>'s <c>--json</c> array.</summary>
internal sealed record SeparationOutput
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("outputFiles")]
    public IReadOnlyList<string>? OutputFiles { get; init; }
}
