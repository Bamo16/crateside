using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Runs one <see cref="WorkOrder"/> through <c>stemforge-cli separate</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One track per invocation, not one batch.</strong> <c>separate</c> accepts many
/// inputs and loops them serially, which is the efficient call — but batching makes "the GPU is
/// busy" and "this track is being worked" two different facts sharing one process id. One track
/// per process costs some repeated start-up and buys far simpler state, plus a pause that takes
/// effect at track boundaries rather than batch boundaries.
/// </para>
/// <para>
/// Shelling out is correct here and does not contradict the preference for managed .NET. That
/// preference is about not reimplementing by scraping — <c>ffprobe</c> for tags where TagLibSharp
/// exists. <c>stemforge-cli</c> is first-party, is the only thing that performs separation, and
/// grew <c>--json</c> so that other tools could drive it.
/// </para>
/// </remarks>
public sealed class SeparationRunner(
    SeparationInterlock interlock,
    WorkingSet workingSet,
    CratesidePaths paths,
    string executable = "stemforge-cli"
)
{
    /// <summary>
    /// Separates one track, start to finish. Refuses rather than queues if the GPU is already
    /// busy: more than one separation at a time brings the machine to a standstill, so this is
    /// the one condition the runner will not work around.
    /// </summary>
    public async Task<SeparationResult> RunAsync(
        WorkOrder order,
        CancellationToken cancellationToken = default
    )
    {
        if (interlock.TryGetLive(out var live))
            return SeparationResult.Refused($"already separating {live.VideoId}");

        var retained = workingSet.TryGetSource(order.VideoId, out var path) ? path : null;

        using var process = Start(order, retained);

        // Claiming needs the process to exist, so there is a sliver between the check above and
        // this line. Harmless here because one worker drives this loop -- but it is why the
        // check and the claim must stay adjacent, and why nothing else may call Start.
        interlock.Claim(order.VideoId, process.Id, StartTimeOf(process));

        try
        {
            // Both reads are started before either is awaited, the same way ExternalProcess
            // does it and for the reason its comment gives: a child that fills one pipe's
            // buffer blocks writing to it, so draining stdout to completion first means a
            // chatty stderr wedges the child forever -- and this is the process that holds
            // the GPU for six minutes. It survives today only because --json keeps stderr
            // quiet, which is a property of the callee, not a guarantee.
            var stdoutRead = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrRead = process.StandardError.ReadToEndAsync(cancellationToken);

            var stdout = await stdoutRead;
            var stderr = await stderrRead;

            await process.WaitForExitAsync(cancellationToken);

            var error = FailureFrom(process.ExitCode, stdout, stderr);

            interlock.Release(order.VideoId, error);

            return error is null
                ? SeparationResult.Succeeded([
                    .. OutputFilesFrom(stdout),
                    .. KeepSource(order, retained),
                ])
                : SeparationResult.Failed(error);
        }
        catch (OperationCanceledException)
        {
            // Pausing must not orphan a running separation the way a restart does: a restart is
            // survivable because the work continues, whereas an abandoned process here would
            // hold the GPU with nobody watching it.
            Stop(process);
            interlock.Release(order.VideoId, "cancelled");

            throw;
        }
    }

    /// <summary>
    /// Puts the source track in the destination itself, for a release whose source <em>is</em>
    /// the sample.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong><c>--keep-source</c> cannot do this, by design.</strong> It is a flag about URL
    /// inputs: the separator downloads a source the caller never had, and the flag says whether
    /// to hand a copy back. Given a local file the caller already has it, so StemForge quite
    /// reasonably ignores the flag — which left an instrumental release producing drums and
    /// nothing else, silently, once crateside started passing the retained file rather than a
    /// URL. Asking the separator to duplicate a file we handed it would be the wrong shape of
    /// fix; owning the copy here is the right one.
    /// </para>
    /// <para>
    /// It carries provenance already, because <c>stemforge-cli download</c> wrote it — so a
    /// plain copy keeps the source URL and therefore the video ID that the handoff stage groups
    /// by. Copy rather than move: the working set is scratch with its own lifetime, and
    /// <see cref="WorkingSetJanitor"/> discards it when the track is imported.
    /// </para>
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
            // The stems are real and the separation did succeed, so this does not fail the run.
            // The track keeps its working set until it is imported, and the copy is retried the
            // next time anything separates it.
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
            // A process that exited immediately may refuse to report one. Recording nothing is
            // honest, and the interlock treats an unverifiable live PID as ours anyway.
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
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        info.ArgumentList.Add("separate");

        // The retained download when there is one, so the track is fetched once for the whole
        // pipeline. Safe only because the file is StemForge's own: it reads the input's
        // provenance comment and carries the source URL into the stems, which is what keeps the
        // video ID -- the join key for the board, for "fully processed", and for queue cleanup.
        // A file from anywhere else would silently produce stems that join to nothing.
        //
        // Asked of the working set rather than carried on the order, because composing an order
        // is pure and this is a question about what is on disk right now. The same shape as
        // VerdictRunner, which asks it the same way for the same reason. WorkOrder used to carry
        // a SourcePath for this and nothing ever assigned it, so every separation silently
        // re-downloaded a file that was already sitting in the working set.
        info.ArgumentList.Add(retained ?? order.SourceUrl);

        foreach (var preset in order.Recipe.Presets)
        {
            info.ArgumentList.Add("--preset");
            info.ArgumentList.Add(preset);
        }

        if (order.Recipe.ExtractDrums)
            info.ArgumentList.Add("--extract-drums");

        // Only meaningful for a URL input, where the separator downloads a source we never had
        // and this says to hand a copy back. Given a local file it is correctly ignored, so
        // passing it there would be a flag that reads as doing something and does not --
        // KeepSource covers that case instead.
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
