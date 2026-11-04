using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;
using Humanizer;

namespace Crateside.Core.Orchestration.Separation;

/// <summary>
/// Runs one <see cref="WorkOrder"/> through <c>stemforge-cli separate</c>.
/// </summary>
public sealed class SeparationRunner(
    SeparationInterlock interlock,
    WorkingSet workingSet,
    CratesidePaths paths,
    string fileName = "stemforge-cli"
)
{
    /// <summary>
    /// Separates one track, start to finish; refuses rather than queues if the GPU is already
    /// busy.
    /// </summary>
    public async Task<SeparationResult> RunAsync(WorkOrder order, CancellationToken ct = default)
    {
        // Stems inherit the source's provenance, which is how the handoff finds them: a retained
        // file without it is fetched again by URL rather than cut into stems that join to nothing.
        var retained =
            workingSet.TryGetSource(order.TrackKey, out var path)
            && StemTagReader.Read(path) is { SourceTrackKey.Length: > 0 }
                ? path
                : null;

        if (interlock.TryGetLive(out var live))
            return SeparationResult.Refused($"already separating {live.TrackKey}");

        // No forceful token: ct must not kill the separation, only stop us waiting for it.
        var running = Start(order, retained);

        // The check above and this claim must stay adjacent, and only this method may call
        // Start — there is a gap between them since claiming needs the process to exist,
        // harmless only because one worker drives this loop.
        interlock.Claim(
            order.TrackKey,
            running.ProcessId,
            StartTimeOf(running.ProcessId),
            new SeparationPromise(order.Recipe.ExpectedOutputs, order.Destination)
        );

        // ct fires on shutdown: the separation keeps running and its job stays open, for the next
        // start to adopt or settle off disk (ADR 0042).
        var result = await running.Task.WaitAsync(ct);
        var stdout = result.StandardOutput;
        var stderr = result.StandardError;

        var error = FailureFrom(result.ExitCode, stdout, stderr);
        IReadOnlyList<string> outputs = [];

        if (error is null)
        {
            outputs = OutputFilesFrom(stdout);
            error = ShortfallFrom(order.Recipe, outputs);
        }

        interlock.Release(
            order.TrackKey,
            error,
            error is null ? Summarize(order.Recipe, outputs.Count) : null
        );

        return error is null ? SeparationResult.Succeeded(outputs) : SeparationResult.Failed(error);
    }

    /// <summary>What a finished run cut, for the activity feed (#170); the feed adds how long it took.</summary>
    internal static string Summarize(SeparationRecipe recipe, int written)
    {
        string[] cut = recipe.ExtractDrums
            ? [.. recipe.Presets, "a drum guide"]
            : [.. recipe.Presets];
        var named = cut switch
        {
            [] => "nothing named",
            [var only] => only,
            [.. var rest, var last] => $"{string.Join(", ", rest)} and {last}",
        };

        return $"cut {named} ({"file".ToQuantity(written)})";
    }

    /// <summary>
    /// Whether the run produced at least what the recipe asked for, said as an error when it did not.
    /// </summary>
    private static string? ShortfallFrom(SeparationRecipe recipe, IReadOnlyList<string> outputs) =>
        outputs.Count < recipe.ExpectedOutputs
            ? $"the recipe asked for {"file".ToQuantity(recipe.ExpectedOutputs)} "
                + $"and the run produced {outputs.Count}"
            : null;

    private static DateTimeOffset StartTimeOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            return process.StartTime;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or ArgumentException or SystemException)
        {
            // A process that exited immediately may refuse to report a start time; the
            // interlock treats an unverifiable PID as ours anyway.
            return default;
        }
    }

    /// <summary>
    /// Reads the outcome out of the run, JSON included, since a zero exit can still carry a
    /// per-input failure.
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

    private static List<SeparationOutput>? Parse(string stdout)
    {
        // The CLI prints a JSON array; anything it logged before it is not ours to interpret.
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize(
                stdout[start..],
                StemForgeJsonContext.Default.ListSeparationOutput
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private CommandTask<BufferedCommandResult> Start(WorkOrder order, string? retained) =>
        Cli.Wrap(fileName)
            .WithArguments(args =>
            {
                args.Add("separate");

                args.Add(retained ?? order.SourceUrl);

                foreach (var preset in order.Recipe.Presets)
                    args.Add("--preset").Add(preset);

                if (order.Recipe.ExtractDrums)
                    args.Add("--extract-drums");

                args.Add("--format").Add(order.Recipe.Format);
                args.Add("--output").Add(paths.DestinationFor(order.Destination));
                args.Add("--json");
            })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(Encoding.UTF8);
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
    public bool Succeeded { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string>? OutputFiles { get; init; }
}
