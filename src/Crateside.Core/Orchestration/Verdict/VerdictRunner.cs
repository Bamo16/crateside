using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// Turns evidence into a verdict: a headless session writes a verdict file, which crateside
/// checks and records (ADR 0055).
/// </summary>
public sealed partial class VerdictRunner(
    LedgerStore store,
    WorkingSet workingSet,
    SourceIndex sources,
    IProcessRunner processes,
    IWindowDetector detector,
    ISourcePages sourcePages,
    IOptions<JudgeOptions> options,
    TimeProvider time,
    ILogger<VerdictRunner> log,
    string fileName = "claude"
) : IVerdictJudge
{
    public const string VerdictFileName = "verdict.json";

    /// <summary>The pages fetched for the track before its first session, kept beside the evidence.</summary>
    public const string SourcesFileName = PageLookups.SourcesFileName;

    private const int MaxRetryAfterSeconds = 3600;

    private const string HighNeeds =
        "HIGH needs backed detectors, a tonic check in favor and a human source that agrees, "
        + "with no authoritative source stating another key";

    internal const string NoAudioLine =
        "crateside holds no audio for this track, so it cannot detect over windows. Do not ask "
        + "for windows or withhold waiting for them: judge from the whole-track evidence.";

    private static readonly string[] Tools = ["Read", "Write", "WebSearch", "WebFetch"];

    private readonly JudgeOptions _options = options.Value;
    private readonly PageLookups _lookups = new(sourcePages, workingSet, store);

    /// <summary>The judge's instructions, the same for every track (judge-prompt.md).</summary>
    public static string Instructions { get; } = ReadInstructions();

    /// <summary>Judges one track: records its verdict, or says why there is none.</summary>
    public Task<VerdictResult> RunAsync(WorkOrder order, CancellationToken ct = default) =>
        RunAsync(order, direction: null, onStep: null, ct);

    public Task<VerdictResult> JudgeAsync(
        WorkOrder order,
        JudgeDirection? direction,
        Action<JudgeStep> onStep,
        CancellationToken ct = default
    ) => RunAsync(order, direction, onStep, ct);

    /// <summary>Judges one track, from the windows the user gave where there is a <paramref name="direction"/> (#116).</summary>
    public async Task<VerdictResult> RunAsync(
        WorkOrder order,
        JudgeDirection? direction,
        Action<JudgeStep>? onStep,
        CancellationToken ct = default
    )
    {
        var step = onStep ?? (_ => { });
        var tally = new SessionTally();

        if (direction is { Audio: null })
            throw new ArgumentException(
                "a run from bars needs its audio found or fetched first",
                nameof(direction)
            );

        if (!workingSet.HasEvidence(order.TrackKey))
        {
            if (direction is null)
                return VerdictResult.Failed("no evidence to judge");

            step(new JudgeStep.DetectingWhole());
            workingSet.Prepare(order.TrackKey);

            var whole = await detector.DetectWholeAsync(
                direction.Audio,
                workingSet.EvidencePathFor(order.TrackKey),
                ct
            );

            if (!whole.IsSuccess)
                return VerdictResult.Failed($"detecting the whole track failed: {whole.Error}");
        }

        var folder = workingSet.Prepare(order.TrackKey);
        var verdictPath = Path.Combine(folder, VerdictFileName);
        var session = Guid.NewGuid().ToString();
        var audio = direction?.Audio ?? SourceFor(order);
        var given = direction is null
            ? []
            : await DetectAsync(direction.Audio, folder, direction.Windows, step, ct);
        var detected = given;
        RunRecord? withheld = null;
        var resumedWithWindows = false;
        var resumedToWrite = false;

        File.Delete(verdictPath);

        var lookups = await _lookups.LookUpAsync(order, ct);

        step(new JudgeStep.Judging());

        var outcome = await SessionAsync(
            ["--session-id", session],
            Prompt(order, verdictPath, lookups, direction, given, audio),
            folder,
            order,
            tally,
            ct
        );

        while (true)
        {
            string prompt;

            switch (
                Assess(
                    VerdictFile.Check(verdictPath),
                    order,
                    direction,
                    audio,
                    detected,
                    lookups,
                    canResume: !resumedToWrite
                )
            )
            {
                case VerdictCheck.Recordable(var run, var note):
                    LogNotes(run, order);

                    return Record(
                        Complete(WithNote(run, note), order, direction, audio, detected) with
                        {
                            WithheldReasoning = withheld?.Reasoning,
                        },
                        order,
                        tally,
                        lookups
                    );

                case VerdictCheck.WindowsAsked(var windows, var run)
                    when !resumedWithWindows && audio is not null:
                    resumedWithWindows = true;
                    LogNotes(run with { Windows = windows }, order);
                    step(new JudgeStep.JudgeAsked(windows));
                    var judged = await DetectAsync(audio, folder, windows, step, ct, given.Count);
                    detected = [.. given, .. judged];
                    withheld = Complete(run, order, direction, audio, detected);
                    prompt = WindowsPrompt(judged, verdictPath);
                    step(new JudgeStep.Judging());
                    break;

                case VerdictCheck.WindowsAsked(_, var run):
                    LogNotes(run, order);

                    return Record(
                        Complete(run, order, direction, audio, detected) with
                        {
                            WithheldReasoning = withheld?.Reasoning,
                        },
                        order,
                        tally,
                        lookups
                    );

                case VerdictCheck.Unusable when Unavailable(outcome) is { } unavailable:
                    return unavailable;

                case VerdictCheck.Unusable(var problem) when !resumedToWrite:
                    resumedToWrite = true;
                    prompt = $"Write the verdict file now, at {verdictPath}. Last time: {problem}.";
                    step(new JudgeStep.Resuming(problem));
                    break;

                case VerdictCheck.Unusable(var problem):
                    return withheld is not null
                        ? Record(withheld, order, tally, lookups)
                        : VerdictResult.Failed(
                            outcome.Succeeded
                                ? problem
                                : ApiError(outcome)
                                    ?? outcome.LastLine
                                    ?? $"{fileName} exited with {outcome.ExitCode}"
                        );

                default:
                    throw new UnreachableException();
            }

            File.Delete(verdictPath);
            outcome = await SessionAsync(["--resume", session], prompt, folder, order, tally, ct);
        }
    }

    /// <summary>The work order, rendered for the session.</summary>
    internal string Prompt(
        WorkOrder order,
        string verdictPath,
        IReadOnlyList<SiteLookup> lookups
    ) => Prompt(order, verdictPath, lookups, direction: null, given: [], SourceFor(order));

    /// <summary>The full mix the order is judged from: the working set's download, else the source the library keeps, by the row's upload or the stated one.</summary>
    public string? SourceFor(WorkOrder order) => sources.PathFor(order);

    private string Prompt(
        WorkOrder order,
        string verdictPath,
        IReadOnlyList<SiteLookup> lookups,
        JudgeDirection? direction,
        IReadOnlyList<DetectedWindow> given,
        string? audio
    )
    {
        List<string> lines =
        [
            "Judge this track.",
            string.Empty,
            $"- title: {order.Title}",
            $"- source_url: {order.SourceUrl}",
            $"- evidence: {workingSet.EvidencePathFor(order.TrackKey)}",
            $"- verdict file: {verdictPath}",
        ];

        if (HeldRun(order) is { Artist: { } artist, Title: { } title })
            lines.Add(
                $"- already judged as artist \"{artist}\", title \"{title}\": write those again"
            );

        lines.AddRange([string.Empty, EarLine(_options.Ear)]);

        if (audio is null)
            lines.AddRange([string.Empty, NoAudioLine]);

        lines.AddRange(PagesLines(lookups));

        if (workingSet.TryReadEvidence(order.TrackKey, out var evidence))
            lines.AddRange([
                string.Empty,
                "The evidence:",
                string.Empty,
                "```json",
                evidence.TrimEnd(),
                "```",
            ]);

        if (direction is not null)
            lines.AddRange(DirectionLines(direction, given));

        return string.Join('\n', lines);
    }

    /// <summary>The user's windows, note and ear, with each window's evidence; the windows become the sections.</summary>
    private static IEnumerable<string> DirectionLines(
        JudgeDirection direction,
        IReadOnlyList<DetectedWindow> given
    )
    {
        yield return string.Empty;

        yield return direction.Windows is []
            ? "The maintainer asked for this track to be judged again, over the whole track."
            : "The maintainer asked for this judgment and named the stretches where they hear the "
                + "key clearly. crateside detected over each. A window is weaker evidence than the "
                + "whole track (see Windows in your instructions), so weigh each against the whole "
                + "file, not above it. Write one section per window, labeled with the window's "
                + "name, with the key you hear in it.";

        if (direction.Note is { Length: > 0 } note)
        {
            yield return string.Empty;
            yield return $"Their note to you: \"{note}\"";
        }

        if (direction.Ear is { } ear)
        {
            yield return string.Empty;
            yield return $"What they hear: {ear.Name}. crateside records it in `sources` with "
                + "kind `ear`; do not add it yourself.";
        }

        foreach (var line in WindowLines(given))
            yield return line;
    }

    /// <summary>The fetched pages, for the judge to accept or reject as this recording.</summary>
    internal static IEnumerable<string> PagesLines(IReadOnlyList<SiteLookup> lookups)
    {
        if (lookups is [])
            yield break;

        yield return string.Empty;
        yield return "Pages crateside found by artist and title before this session, read for you. "
            + "Whether each is this recording is yours to decide; one you rely on goes in `sources` "
            + "like any other.";
        yield return string.Empty;

        foreach (var page in lookups.SelectMany(lookup => lookup.Pages))
        {
            var about = string.Join(", ", new[] { page.Arrangement, page.Note }.OfType<string>());
            var keys = page.Keys is []
                ? "no key stated"
                : string.Join(
                    "; ",
                    page.Keys.Select(key =>
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{(key.Section is { } section ? $"{section}: " : string.Empty)}{key.Key}{(key.Tempo is { } tempo ? $", {tempo} BPM" : string.Empty)}"
                        )
                    )
                );

            yield return $"- {page.Site}, {page.Url}{(about is "" ? string.Empty : $" ({about})")}: "
                + $"\"{page.Heading}\". {keys}";

            if (page.Tab is { } tab)
            {
                foreach (var line in TabLines(tab))
                    yield return $"  - {line}";
            }
        }

        foreach (var lookup in lookups.Where(lookup => lookup.Status is not LookupStatus.Found))
            yield return lookup.Status is LookupStatus.NotFound
                ? $"- {lookup.Site}: nothing found"
                : $"- {lookup.Site}: not reached ({lookup.Error})";
    }

    /// <summary>What a TheoryTab states beyond its sections' first keys: who made it, how much of the recording it maps, and where a section modulates.</summary>
    private static IEnumerable<string> TabLines(HooktheoryTab tab)
    {
        var sections = string.Join(", ", tab.Sections.Select(section => section.Name));
        var wholeSong = tab.AllKeys is { Length: > 0 } keys
            ? $"; the site lists the whole song's keys as {keys}"
            : string.Empty;

        yield return tab.Contributors is []
            ? $"no contributor named; {"section".ToQuantity(tab.Sections.Count)} ({sections}){wholeSong}"
            : $"{"contributor".ToQuantity(tab.Contributors.Count)} ({string.Join(", ", tab.Contributors)}); "
                + $"{"section".ToQuantity(tab.Sections.Count)} ({sections}){wholeSong}";

        if (tab.IsThin)
            yield return "THIN PAGE: one contributor and one or two sections";

        var other = tab.Sections.Count(section => section.TrackKey is not null);
        var synced = tab.SyncedTrackKey is { } video
            ? $"synced to YouTube {video}"
            : "not synced to a video";

        yield return (tab.Coverage, tab.Sections.Any(section => section.Keys is not [])) switch
        {
            ({ } coverage, _) => string.Create(
                CultureInfo.InvariantCulture,
                $"{synced}; its sections span {coverage * 100:0.0}% of that video{(other is 0 ? string.Empty : $", and {"section".ToQuantity(other)} are synced to other videos")}"
            ),
            (null, true) => $"{synced}; no section is synced to it, so its coverage is unknown",
            _ => $"{synced}; the sections' own scores could not be read, so their sync is unknown",
        };

        foreach (var section in tab.Sections)
        {
            var filled = (section.PercentChord, section.PercentMelody) switch
            {
                ({ } chords, { } melody) => string.Create(
                    CultureInfo.InvariantCulture,
                    $" (chords {chords:0}% filled in, melody {melody:0}%)"
                ),
                _ => string.Empty,
            };

            if (section is { Keys.Count: > 1, Harmony: [_, ..] })
                yield return $"{section.Name}, its chords by its score, since the page's numerals stay in its first key: "
                    + $"{HarmonyByKey(section)}{filled}";
            else if (section.Progression is { Length: > 0 } progression)
                yield return $"{section.Name}: {progression}{filled}";

            if (section.Keys is { Count: > 1 } changes)
                yield return $"{section.Name} changes key inside the section: "
                    + string.Join(
                        ", then ",
                        changes.Select(change =>
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{change.Key} from beat {change.Beat}"
                            )
                        )
                    );
        }
    }

    /// <summary><c>E major from beat 1: E:maj B:maj | E minor from beat 9: C:maj D:maj E:min</c>, repeats collapsed as the page collapses them.</summary>
    private static string HarmonyByKey(TabSection section)
    {
        var keys = section.Keys.OrderBy(key => key.Beat).ToList();
        List<string> segments = [];

        foreach (var (key, index) in keys.Select((key, index) => (key, index)))
        {
            List<string> labels =
            [
                .. section
                    .Harmony.Where(chord =>
                        (index == 0 || chord.Beat >= key.Beat)
                        && (index == keys.Count - 1 || chord.Beat < keys[index + 1].Beat)
                    )
                    .Select(chord => chord.Label),
            ];

            if (labels is not [])
                segments.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{key.Key} from beat {key.Beat}: {string.Join(' ', Collapsed(labels))}"
                    )
                );
        }

        return string.Join(" | ", segments);
    }

    private static IEnumerable<string> Collapsed(List<string> labels) =>
        labels.Where((label, index) => index == 0 || labels[index - 1] != label);

    internal static string EarLine(EarWeight ear) =>
        ear switch
        {
            EarWeight.HumanSource =>
                "The maintainer's own reading of the key, where given (crateside records it with kind `ear`), "
                    + "counts like an authoritative source: a dissent from it caps the verdict at "
                    + "MEDIUM, and agreement with a backed keydetect can reach HIGH.",
            _ =>
                "The maintainer's own reading of the key, where given (crateside records it with kind `ear`), "
                    + "is a hint: weigh it, but it never raises confidence by itself.",
        };

    private static string WindowsPrompt(IReadOnlyList<DetectedWindow> detected, string verdictPath)
    {
        List<string> lines =
        [
            $"crateside detected over the windows you asked for. Judge again with this evidence and write the verdict file at {verdictPath}.",
            .. WindowLines(detected),
        ];

        return string.Join('\n', lines);
    }

    private static IEnumerable<string> WindowLines(IReadOnlyList<DetectedWindow> detected)
    {
        foreach (var (window, evidence, error) in detected)
        {
            yield return string.Empty;
            yield return $"Window {Describe(window)}:";
            yield return evidence is { } json
                ? $"```json\n{json.TrimEnd()}\n```"
                : $"detection failed: {error}";
        }
    }

    private static string Describe(RunWindow window) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"\"{window.Name}\", {window.Start:0.##}–{window.End:0.##} s"
        );

    private async Task<ProcessOutcome> SessionAsync(
        IReadOnlyList<string> session,
        string prompt,
        string folder,
        WorkOrder order,
        SessionTally tally,
        CancellationToken ct
    )
    {
        var outcome = await processes.RunAsync(
            new ProcessRequest(
                fileName,
                [
                    "-p",
                    .. session,
                    "--model",
                    _options.Model,
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
                    // Keeps the user's CLAUDE.md, rules and memory (~45k characters) out of every session.
                    "--safe-mode",
                    // The working set holds no settings, so this loads none, and none of the user's.
                    "--setting-sources",
                    "project",
                    "--max-budget-usd",
                    "2.00",
                ]
            )
            {
                WorkingDirectory = folder,
                Environment = new Dictionary<string, string>(_options.Environment)
                {
                    // Every tool's schema up front, never a deferred placeholder.
                    ["ENABLE_TOOL_SEARCH"] = "false",
                },
                StandardInput = prompt,
            },
            ct
        );

        LogSession(outcome, order, tally);

        return outcome;
    }

    /// <summary>Turns and time per session, the measure of a judge besides its verdicts.</summary>
    private void LogSession(ProcessOutcome outcome, WorkOrder order, SessionTally tally)
    {
        if (TryParseResult(outcome.StandardOutput) is not { } result)
            return;

        tally.Turns += result["num_turns"]?.GetValue<int>() ?? 0;
        tally.Seconds += result["duration_ms"]?.GetValue<double>() / 1000 ?? 0;

        log.LogInformation(
            "Judge session for {TrackKey} on {Model}: {Turns} turns, {Seconds:0} s",
            order.TrackKey,
            _options.Model,
            result["num_turns"]?.GetValue<int>(),
            result["duration_ms"]?.GetValue<double>() / 1000
        );
    }

    private static JsonNode? TryParseResult(string? output)
    {
        try
        {
            return output is { Length: > 0 } ? JsonNode.Parse(output) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <param name="first">How many windows this run has already detected, so their files are kept.</param>
    private async Task<List<DetectedWindow>> DetectAsync(
        string audio,
        string folder,
        IReadOnlyList<RunWindow> windows,
        Action<JudgeStep> step,
        CancellationToken ct,
        int first = 0
    )
    {
        List<DetectedWindow> detected = [];

        foreach (var (window, index) in windows.Select((window, index) => (window, index)))
        {
            var path = Path.Combine(folder, $"evidence.window{first + index + 1}.json");

            step(new JudgeStep.DetectingWindow(index + 1, windows.Count, window));
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

    /// <summary>
    /// A recordable run the ledger would refuse, or whose key the evidence never held, is unusable,
    /// so the session is told why; a HIGH without its backing is capped once the session has had its resume (ADR 0069).
    /// </summary>
    private VerdictCheck Assess(
        VerdictCheck check,
        WorkOrder order,
        JudgeDirection? direction,
        string? audio,
        IReadOnlyList<DetectedWindow> detected,
        IReadOnlyList<SiteLookup> lookups,
        bool canResume
    )
    {
        if (check is not VerdictCheck.Recordable(var run, _))
            return check;

        var complete = RunValidation.Normalize(Complete(run, order, direction, audio, detected));

        if (RunValidation.Refusal(complete) is { } refusal)
            return new VerdictCheck.Unusable($"{refusal.Field}: {refusal.Problem}");

        if (RecordTimeCheck.UnfoundKey(complete, lookups) is { } unfound)
            return new VerdictCheck.Unusable(unfound);

        if (RecordTimeCheck.MissingForHigh(complete, _options.Ear, lookups) is not { } missing)
            return check;

        return canResume
            ? new VerdictCheck.Unusable(
                $"confidence: {HighNeeds}; missing {missing}. "
                    + "Write the confidence the evidence supports, or add what is missing."
            )
            : new VerdictCheck.Recordable(
                run with
                {
                    Confidence = KeyConfidence.Medium,
                },
                $"crateside capped this verdict at MEDIUM: {HighNeeds}, and it lacked {missing}."
            );
    }

    private static RunRecord WithNote(RunRecord run, string? note) =>
        note is null
            ? run
            : run with
            {
                Reasoning = string.Join("\n\n", new[] { run.Reasoning, note }.OfType<string>()),
            };

    /// <summary>The run, with what crateside knows and the session cannot.</summary>
    private RunRecord Complete(
        RunRecord run,
        WorkOrder order,
        JudgeDirection? direction,
        string? audio,
        IReadOnlyList<DetectedWindow> detected
    )
    {
        workingSet.TryReadEvidence(order.TrackKey, out var evidence);

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
            Direction = direction?.Note,
            Sources = direction?.Ear is { } ear
                ?
                [
                    .. run.Sources.Where(source => source.Kind is not SourceKind.Ear),
                    EarSource(ear),
                ]
                : run.Sources,
            Evidence = Evidence(evidence, detected),
        };
    }

    /// <summary>What the user said they hear, as a source, with how much it counted on the day.</summary>
    private RunSource EarSource(TonalCenter ear) =>
        new()
        {
            Via = "the maintainer's ear",
            Kind = SourceKind.Ear,
            States = ear.Name,
            StatesKey = ear.Collapsed,
            StatesMode = ear.Mode,
            Note =
                _options.Ear is EarWeight.HumanSource
                    ? "counted as a human source"
                    : "weighed as a hint",
        };

    private static string? Evidence(string? given, IReadOnlyList<DetectedWindow> detected)
    {
        var whole = given is null ? null : LateReading.Unstamped(given);

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

    private LedgerRun? HeldRun(WorkOrder order) => _lookups.HeldRun(order);

    private void LogNotes(RunRecord run, WorkOrder order)
    {
        foreach (var note in RunValidation.Notes(run))
            log.LogWarning("Verdict for {TrackKey}: {Note}", order.TrackKey, note);
    }

    /// <param name="lookups">Kept with the run, cited or not (#176).</param>
    private VerdictResult Record(
        RunRecord run,
        WorkOrder order,
        SessionTally tally,
        IReadOnlyList<SiteLookup> lookups
    )
    {
        if (
            store.Record(run with { Lookups = VerdictRecording.Serialized(lookups) })
            is RecordOutcome.Refused(var field, var problem)
        )
            return VerdictResult.Failed($"the ledger refused the verdict: {field}: {problem}");

        VerdictRecording.LinkSwappedSource(store, order);

        return VerdictResult.Succeeded(Summarize(run, _options.Model, tally));
    }

    /// <summary>The verdict a run recorded and what the judge spent on it, for the activity feed (#170).</summary>
    internal static string Summarize(RunRecord run, string model, SessionTally tally)
    {
        var verdict = run.Center is { Name: var name }
            ? $"{name}, {run.Confidence.ToString().ToUpperInvariant()}"
            : $"key withheld, {run.Confidence.ToString().ToUpperInvariant()}";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{verdict}, {"section".ToQuantity(run.Sections.Count)}; {"turn".ToQuantity(tally.Turns)} on {model}, {tally.Seconds:0} s"
        );
    }

    /// <summary>
    /// Why no session could be served, read from its prose as well as its JSON, or null when the
    /// failure is about this track (ADR 0070).
    /// </summary>
    private VerdictResult? Unavailable(ProcessOutcome outcome)
    {
        string[] streams = [outcome.StandardOutput, outcome.StandardError];
        var status = ApiErrorStatus(streams);

        if (status is 429 || Mentions(streams, "hit your limit"))
            return _options.SpendsClaudeQuota
                ? VerdictResult.AgentUnavailable(
                    "the Claude allowance is spent; judging is paused until it resets"
                )
                : Refused("the judge's endpoint reported its rate limit");

        // Not a bare "/login", which any searched-up URL could carry.
        if (Mentions(streams, "not logged in", "please run /login"))
            return _options.SpendsClaudeQuota
                ? VerdictResult.AgentUnavailable(
                    "Claude Code is not logged in; judging is paused until you run /login"
                )
                : Refused("Claude Code is not logged in; run /login");

        // Claude Code retries for minutes before giving up, so this is no blip.
        if (Mentions(streams, "ECONNREFUSED", "Unable to connect to API"))
            return Refused("the judge's endpoint cannot be reached");

        if (Mentions(streams, "Failed to authenticate"))
            return Refused("the judge's endpoint refused its key");

        var error = ApiError(outcome);

        if (
            status is 529
            || error?.Contains("overloaded", StringComparison.OrdinalIgnoreCase) is true
        )
            return Refused("the judge's endpoint is overloaded");

        if (status is >= 500)
            return Refused($"the judge's endpoint failed with HTTP {status}");

        // A request the endpoint could not take is this track's, and would be refused again.
        return error is not null && status is not (400 or 413 or 422)
            ? Refused($"the judge's endpoint answered \"{error}\"")
            : null;

        VerdictResult Refused(string reason) => VerdictResult.Refused(reason, RetryAfter(streams));
    }

    private static int? ApiErrorStatus(string[] streams) =>
        streams
            .Select(stream => StatusPattern.Match(stream ?? string.Empty))
            .FirstOrDefault(match => match.Success)
            is { Groups: var groups }
        && int.TryParse(groups["Status"].Value, CultureInfo.InvariantCulture, out var status)
            ? status
            : null;

    [GeneratedRegex(@"""api_error_status"":\s*(?<Status>\d{3})", RegexOptions.ExplicitCapture)]
    private static partial Regex StatusPattern { get; }

    /// <summary>The wait the endpoint stated with its rate limit, or null when it gave none.</summary>
    private static TimeSpan? RetryAfter(string[] streams) =>
        streams
            .Select(stream => StatedPause.Match(stream ?? string.Empty))
            .FirstOrDefault(match => match.Success)
            is { Groups: var groups }
        && int.TryParse(groups["Seconds"].Value, out var seconds)
            ? TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryAfterSeconds))
            : null;

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

    [GeneratedRegex(
        @"\bpause for (?<Seconds>\d+)\s+(more\s+)?seconds",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture
    )]
    private static partial Regex StatedPause { get; }

    /// <summary>
    /// The endpoint's own error from the session's result, which Claude Code's stderr can misname
    /// (any 422 prints as "unrecognized_model").
    /// </summary>
    private static string? ApiError(ProcessOutcome outcome) =>
        SessionApiError.Match(outcome.StandardOutput) is { Success: true } match
            ? match.Groups["Message"].Value
            : null;

    [GeneratedRegex(@"""result"":""(?<Message>API Error:[^""]*)""", RegexOptions.ExplicitCapture)]
    private static partial Regex SessionApiError { get; }

    private sealed record DetectedWindow(RunWindow Window, string? Evidence, string? Error);

    /// <summary>What a run's judge sessions spent, added up as they end.</summary>
    internal sealed class SessionTally
    {
        public int Turns { get; set; }

        public double Seconds { get; set; }
    }
}

/// <summary>How one verdict run turned out.</summary>
public sealed record VerdictResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// No session could be served: the allowance is spent, nobody is logged in, or the endpoint
    /// refused. Says nothing about the track.
    /// </summary>
    public bool IsAgentUnavailable { get; init; }

    /// <summary>The endpoint refused or failed the session, which holds judging rather than pausing it (ADR 0070).</summary>
    public bool IsRefused { get; init; }

    /// <summary>How long the endpoint said to wait, where it said.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public string? Error { get; init; }

    /// <summary>The verdict recorded and what the judge spent, in a line for the activity feed (#170). Null unless it succeeded.</summary>
    public string? Summary { get; init; }

    public static VerdictResult Succeeded(string? summary = null) =>
        new() { IsSuccess = true, Summary = summary };

    public static VerdictResult Failed(string error) => new() { IsSuccess = false, Error = error };

    public static VerdictResult AgentUnavailable(string reason) =>
        new()
        {
            IsSuccess = false,
            IsAgentUnavailable = true,
            Error = reason,
        };

    public static VerdictResult Refused(string reason, TimeSpan? retryAfter) =>
        AgentUnavailable(reason) with
        {
            IsRefused = true,
            RetryAfter = retryAfter,
        };
}
