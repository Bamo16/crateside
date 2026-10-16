using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// Turns evidence into a verdict: a headless session writes a verdict file, which crateside
/// checks and records (ADR 0055).
/// </summary>
public sealed class VerdictRunner(
    LedgerStore store,
    WorkingSet workingSet,
    IProcessRunner processes,
    IWindowDetector detector,
    JudgeOptions options,
    TimeProvider time,
    string fileName = "claude"
)
{
    public const string VerdictFileName = "verdict.json";

    private static readonly string[] Tools = ["Read", "Write", "WebSearch", "WebFetch"];

    /// <summary>The judge's instructions, the same for every track (judge-prompt.md).</summary>
    public static string Instructions { get; } = ReadInstructions();

    /// <summary>Judges one track: records its verdict, or says why there is none.</summary>
    public async Task<VerdictResult> RunAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (!workingSet.HasEvidence(order.VideoId))
            return VerdictResult.Failed("no evidence to judge");

        var folder = workingSet.Prepare(order.VideoId);
        var verdictPath = Path.Combine(folder, VerdictFileName);
        var session = Guid.NewGuid().ToString();
        List<DetectedWindow> detected = [];
        RunRecord? withheld = null;
        var resumedWithWindows = false;
        var resumedToWrite = false;

        File.Delete(verdictPath);

        var outcome = await SessionAsync(
            ["--session-id", session],
            Prompt(order, verdictPath),
            folder,
            ct
        );

        while (true)
        {
            string prompt;

            switch (Assess(VerdictFile.Check(verdictPath), order, detected))
            {
                case VerdictCheck.Recordable(var run):
                    return Record(Complete(run, order, detected), order);

                case VerdictCheck.WindowsAsked(var windows, var run)
                    when !resumedWithWindows
                        && workingSet.TryGetSource(order.VideoId, out var audio):
                    resumedWithWindows = true;
                    detected = await DetectAsync(audio, folder, windows, ct);
                    withheld = Complete(run, order, detected);
                    prompt = WindowsPrompt(detected, verdictPath);
                    break;

                case VerdictCheck.WindowsAsked(_, var run):
                    return Record(Complete(run, order, detected), order);

                case VerdictCheck.Unusable when AgentUnavailableReason(outcome) is { } reason:
                    return VerdictResult.AgentUnavailable(reason);

                case VerdictCheck.Unusable(var problem) when !resumedToWrite:
                    resumedToWrite = true;
                    prompt = $"Write the verdict file now, at {verdictPath}. Last time: {problem}.";
                    break;

                case VerdictCheck.Unusable(var problem):
                    return withheld is not null
                        ? Record(withheld, order)
                        : VerdictResult.Failed(
                            outcome.Succeeded
                                ? problem
                                : ExternalProcess.Describe(
                                    outcome.StandardError,
                                    outcome.StandardOutput
                                ) ?? $"{fileName} exited with {outcome.ExitCode}"
                        );

                default:
                    throw new UnreachableException();
            }

            File.Delete(verdictPath);
            outcome = await SessionAsync(["--resume", session], prompt, folder, ct);
        }
    }

    /// <summary>The work order, rendered for the session.</summary>
    internal string Prompt(WorkOrder order, string verdictPath)
    {
        List<string> lines =
        [
            "Judge this track.",
            string.Empty,
            $"- title: {order.Title}",
            $"- source_url: {order.SourceUrl}",
            $"- evidence: {workingSet.EvidencePathFor(order.VideoId)}",
            $"- verdict file: {verdictPath}",
        ];

        if (HeldRun(order) is { Artist: { } artist, Title: { } title })
            lines.Add(
                $"- already judged as artist \"{artist}\", title \"{title}\": write those again"
            );

        lines.AddRange([string.Empty, EarLine(options.Ear)]);

        if (workingSet.TryReadEvidence(order.VideoId, out var evidence))
            lines.AddRange([
                string.Empty,
                "The evidence:",
                string.Empty,
                "```json",
                evidence.TrimEnd(),
                "```",
            ]);

        return string.Join('\n', lines);
    }

    internal static string EarLine(EarWeight ear) =>
        ear switch
        {
            EarWeight.HumanSource =>
                "The maintainer's own reading of the key, where given (record it with kind `ear`), "
                    + "counts like an authoritative source: a dissent from it caps the verdict at "
                    + "MEDIUM, and agreement with a backed keydetect can reach HIGH.",
            _ =>
                "The maintainer's own reading of the key, where given (record it with kind `ear`), "
                    + "is a hint: weigh it, but it never raises confidence by itself.",
        };

    private static string WindowsPrompt(IReadOnlyList<DetectedWindow> detected, string verdictPath)
    {
        List<string> lines =
        [
            $"crateside detected over the windows you asked for. Judge again with this evidence and write the verdict file at {verdictPath}.",
        ];

        foreach (var (window, evidence, error) in detected)
        {
            lines.AddRange([string.Empty, $"Window {Describe(window)}:"]);
            lines.Add(
                evidence is { } json
                    ? $"```json\n{json.TrimEnd()}\n```"
                    : $"detection failed: {error}"
            );
        }

        return string.Join('\n', lines);
    }

    private static string Describe(RunWindow window) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{window.Start}–{window.End} s{(window.Label is { } label ? $" ({label})" : string.Empty)}"
        );

    private async Task<ProcessOutcome> SessionAsync(
        IReadOnlyList<string> session,
        string prompt,
        string folder,
        CancellationToken ct
    ) =>
        await processes.RunAsync(
            new ProcessRequest(
                fileName,
                [
                    "-p",
                    .. session,
                    "--model",
                    options.Model,
                    "--output-format",
                    "json",
                    "--append-system-prompt",
                    Instructions,
                    "--tools",
                    string.Join(',', Tools),
                    "--allowed-tools",
                    .. Tools,
                    "--permission-mode",
                    "acceptEdits",
                    "--strict-mcp-config",
                    // The working set holds no settings, so this loads none, and none of the user's.
                    "--setting-sources",
                    "project",
                    "--max-budget-usd",
                    "2.00",
                ]
            )
            {
                WorkingDirectory = folder,
                Environment = options.Environment,
                StandardInput = prompt,
            },
            ct
        );

    private async Task<List<DetectedWindow>> DetectAsync(
        string audio,
        string folder,
        IReadOnlyList<RunWindow> windows,
        CancellationToken ct
    )
    {
        List<DetectedWindow> detected = [];

        foreach (var (window, index) in windows.Select((window, index) => (window, index)))
        {
            var path = Path.Combine(folder, $"evidence.window{index + 1}.json");

            File.Delete(path);

            var result = await detector.DetectWindowAsync(audio, window, path, ct);

            detected.Add(
                result.IsSuccess
                    ? new(window, File.ReadAllText(path), null)
                    : new(window, null, result.Error)
            );
        }

        return detected;
    }

    /// <summary>A recordable run the ledger would still refuse is unusable, so the session is told why.</summary>
    private VerdictCheck Assess(
        VerdictCheck check,
        WorkOrder order,
        IReadOnlyList<DetectedWindow> detected
    ) =>
        check is VerdictCheck.Recordable(var run)
        && RunValidation.Refusal(RunValidation.Normalize(Complete(run, order, detected)))
            is { } refusal
            ? new VerdictCheck.Unusable($"{refusal.Field}: {refusal.Problem}")
            : check;

    /// <summary>The run, with what crateside knows and the session cannot.</summary>
    private RunRecord Complete(
        RunRecord run,
        WorkOrder order,
        IReadOnlyList<DetectedWindow> detected
    )
    {
        workingSet.TryGetSource(order.VideoId, out var audio);
        workingSet.TryReadEvidence(order.VideoId, out var evidence);

        return run with
        {
            RecordedAt = time.GetUtcNow(),
            Song = HeldRun(order)?.Song ?? run.Song,
            SourceUrl = order.SourceUrl,
            InputPath = audio,
            InputKind = audio is null ? null : "full_mix",
            Windows =
            [
                .. detected
                    .Where(window => window.Evidence is not null)
                    .Select(window => window.Window),
            ],
            Evidence = Evidence(evidence, detected),
        };
    }

    private static string? Evidence(string? whole, IReadOnlyList<DetectedWindow> detected)
    {
        if (whole is null || detected is [] || JsonNode.Parse(whole) is not JsonObject root)
            return whole;

        root["windows"] = new JsonArray([
            .. detected
                .Where(window => window.Evidence is not null)
                .Select(window => new JsonObject
                {
                    ["start"] = window.Window.Start,
                    ["end"] = window.Window.End,
                    ["label"] = window.Window.Label,
                    ["evidence"] = JsonNode.Parse(window.Evidence!),
                }),
        ]);

        return root.ToJsonString();
    }

    /// <summary>The run already recorded for this upload, or for the source it was swapped to.</summary>
    private LedgerRun? HeldRun(WorkOrder order)
    {
        var ledger = store.Read();

        return YouTubeUrl.TryGetVideoId(order.SourceUrl, out var source)
            && ledger.TryGetLatestByVideoId(source, out var swapped)
                ? swapped
            : ledger.TryGetLatestByVideoId(order.VideoId, out var held) ? held
            : null;
    }

    private VerdictResult Record(RunRecord run, WorkOrder order)
    {
        if (store.Record(run) is RecordOutcome.Refused(var field, var problem))
            return VerdictResult.Failed($"the ledger refused the verdict: {field}: {problem}");

        LinkSwappedSource(order);

        return VerdictResult.Succeeded();
    }

    /// <summary>
    /// A source swapped before its verdict is recorded under the stated upload, so the row's own
    /// ID is linked to that song, as the swap does for a judged track (#88).
    /// </summary>
    private void LinkSwappedSource(WorkOrder order)
    {
        if (
            YouTubeUrl.TryGetVideoId(order.SourceUrl, out var source)
            && !source.Equals(order.VideoId, StringComparison.Ordinal)
        )
            store.Restate(source, $"https://music.youtube.com/watch?v={order.VideoId}");
    }

    /// <summary>
    /// Why the agent could not be run, read from its prose as well as its JSON, or null when the
    /// failure is about this track.
    /// </summary>
    private static string? AgentUnavailableReason(ProcessOutcome outcome) =>
        new[] { outcome.StandardOutput, outcome.StandardError } switch
        {
            var streams when Mentions(streams, "\"api_error_status\":429", "hit your limit") =>
                "the Claude allowance is spent; judging is paused until it resets",
            // Not a bare "/login", which any searched-up URL could carry.
            var streams when Mentions(streams, "not logged in", "please run /login") =>
                "Claude Code is not logged in; judging is paused until you run /login",
            _ => null,
        };

    private static bool Mentions(string[] streams, params string[] phrases) =>
        streams.Any(stream =>
            phrases.Any(phrase => stream.Contains(phrase, StringComparison.OrdinalIgnoreCase))
        );

    private static string ReadInstructions()
    {
        using var stream = typeof(VerdictRunner).Assembly.GetManifestResourceStream(
            "judge-prompt.md"
        )!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private sealed record DetectedWindow(RunWindow Window, string? Evidence, string? Error);
}

/// <summary>How one verdict run turned out.</summary>
public sealed record VerdictResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// The agent never ran — no allowance left, or nobody logged in. Says nothing about the
    /// track, and a further attempt would fail the same way and just as fast.
    /// </summary>
    public bool IsAgentUnavailable { get; init; }

    public string? Error { get; init; }

    public static VerdictResult Succeeded() => new() { IsSuccess = true };

    public static VerdictResult Failed(string error) => new() { IsSuccess = false, Error = error };

    public static VerdictResult AgentUnavailable(string reason) =>
        new()
        {
            IsSuccess = false,
            IsAgentUnavailable = true,
            Error = reason,
        };
}
