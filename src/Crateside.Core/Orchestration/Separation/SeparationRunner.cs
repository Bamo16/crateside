using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Plans;
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

        var ownKey = IsOwnKey(order);

        // An added source's guide needs none of the track's own audio.
        var cutsOwn = order.Recipe.CutsOwnSource;

        if (cutsOwn && ownKey && !workingSet.TryGetSource(order.TrackKey, out retained))
            return SeparationResult.Failed($"the audio for {order.TrackKey} is not on disk");

        if (cutsOwn && ownKey && !await SupportsSourceUrlAsync(ct))
            return SeparationResult.Failed(StemForgeTooOld);

        List<string> outputs = [];
        string? error = null;

        if (cutsOwn)
            (error, outputs) = await RunProcessAsync(order, Start(order, retained, ownKey), ct);

        // A swap throws away the retained download this run was reading, so its failure is the swap's doing (#205).
        if (error is not null && retained is { } read && !File.Exists(read))
        {
            interlock.Cancel(order.TrackKey, SourceSwapped);

            return SeparationResult.Canceled(SourceSwapped);
        }

        // One run per added source, after the track's own: the separator takes one input a run.
        foreach (var guide in order.Recipe.AddedGuides)
        {
            if (error is not null)
                break;

            (error, var guideOutputs) = await RunProcessAsync(order, StartGuide(order, guide), ct);

            outputs.AddRange(guideOutputs);
        }

        error ??= ShortfallFrom(order.Recipe, outputs);

        interlock.Release(
            order.TrackKey,
            error,
            error is null ? Summarize(order.Recipe, outputs.Count) : null
        );

        return error is null ? SeparationResult.Succeeded(outputs) : SeparationResult.Failed(error);
    }

    /// <summary>
    /// Claims the GPU for a started process and waits for it, answering what it failed with or
    /// the files it wrote.
    /// </summary>
    private async Task<(string? Error, List<string> Outputs)> RunProcessAsync(
        WorkOrder order,
        CommandTask<BufferedCommandResult> running,
        CancellationToken ct
    )
    {
        // The check before this and this claim must stay adjacent, and only RunAsync may call
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

        return
            FailureFrom(result.ExitCode, result.StandardOutput, result.StandardError) is { } failure
            ? (failure, [])
            : (null, [.. OutputFilesFrom(result.StandardOutput)]);
    }

    public const string SourceSwapped = "source swapped";

    /// <summary>The message when the installed stemforge-cli cannot be told which track a file is.</summary>
    public const string StemForgeTooOld =
        "StemForge is too old: stemforge-cli separate needs --source-url. "
        + "Update stemforge-cli (StemForge's install task), then retry.";

    private bool _supportsSourceUrl;

    /// <summary>A track not on YouTube, cut from audio crateside holds and tells StemForge the key of (ADR 0077).</summary>
    private static bool IsOwnKey(WorkOrder order) =>
        !TrackKeys.IsVideoId(order.TrackKey)
        && order.SourceUrl.Equals(TrackKeys.ProvenanceOf(order.TrackKey), StringComparison.Ordinal);

    private async Task<bool> SupportsSourceUrlAsync(CancellationToken ct)
    {
        if (_supportsSourceUrl)
            return true;

        var help = await Cli.Wrap(fileName)
            .WithArguments(["separate", "--help"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(Encoding.UTF8, ct);

        return _supportsSourceUrl = help.StandardOutput.Contains(
            "--source-url",
            StringComparison.Ordinal
        );
    }

    /// <summary>What a finished run cut, for the activity feed (#170); the feed adds how long it took.</summary>
    internal static string Summarize(SeparationRecipe recipe, int written)
    {
        string[] cut =
        [
            .. recipe.Presets,
            .. recipe.ExtractDrums ? (string[])["a drum guide"] : [],
            .. recipe.AddedGuides.Select(guide => $"a drum guide for {guide.Label}"),
        ];
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
    private static string? ShortfallFrom(SeparationRecipe recipe, List<string> outputs) =>
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

    /// <summary>Drums only, from the added file: it carries its own provenance, which the guide inherits.</summary>
    private CommandTask<BufferedCommandResult> StartGuide(WorkOrder order, AddedGuide guide) =>
        Cli.Wrap(fileName)
            .WithArguments(args =>
                args.Add("separate")
                    .Add(guide.FilePath)
                    .Add("--extract-drums")
                    .Add("--format")
                    .Add(order.Recipe.Format)
                    .Add("--output")
                    .Add(paths.DestinationFor(order.Destination))
                    .Add("--json")
            )
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(Encoding.UTF8);

    private CommandTask<BufferedCommandResult> Start(
        WorkOrder order,
        string? retained,
        bool ownKey
    ) =>
        Cli.Wrap(fileName)
            .WithArguments(args =>
            {
                args.Add("separate");

                args.Add(retained ?? order.SourceUrl);

                if (ownKey)
                    args.Add("--source-url").Add(order.SourceUrl);

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

    public static SeparationResult Canceled(string reason) =>
        new() { Outcome = SeparationOutcome.Canceled, Error = reason };
}

public enum SeparationOutcome
{
    Succeeded,
    Failed,

    /// <summary>Not attempted, because the GPU was already busy. Not a failure of the track.</summary>
    Refused,

    /// <summary>Stopped by an edit to the track, a source swap; the track is owed a run, nothing failed.</summary>
    Canceled,
}

/// <summary>One element of <c>stemforge-cli</c>'s <c>--json</c> array.</summary>
internal sealed record SeparationOutput
{
    public bool Succeeded { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string>? OutputFiles { get; init; }
}
