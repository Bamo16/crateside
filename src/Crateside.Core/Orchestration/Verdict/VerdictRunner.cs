using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Humanizer;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// Turns evidence into a verdict: a headless session writes a verdict file, which crateside
/// checks and records (ADR 0055).
/// </summary>
public sealed partial class VerdictRunner(
    LedgerStore store,
    WorkingSet workingSet,
    IProcessRunner processes,
    IWindowDetector detector,
    ISourcePages sourcePages,
    JudgeOptions options,
    TimeProvider time,
    string fileName = "claude"
)
{
    public const string VerdictFileName = "verdict.json";

    /// <summary>The pages fetched for the track before its first session, kept beside the evidence.</summary>
    public const string SourcesFileName = "sources.json";

    private static readonly string[] Tools = ["Read", "Write", "WebSearch", "WebFetch"];

    /// <summary>The judge's instructions, the same for every track (judge-prompt.md).</summary>
    public static string Instructions { get; } = ReadInstructions();

    /// <summary>Judges one track: records its verdict, or says why there is none.</summary>
    public Task<VerdictResult> RunAsync(WorkOrder order, CancellationToken ct = default) =>
        RunAsync(order, direction: null, onStep: null, ct);

    /// <summary>Judges one track, from the windows the user gave where there is a <paramref name="direction"/> (#116).</summary>
    public async Task<VerdictResult> RunAsync(
        WorkOrder order,
        JudgeDirection? direction,
        Action<JudgeStep>? onStep,
        CancellationToken ct = default
    )
    {
        var step = onStep ?? (_ => { });

        if (direction is { Audio: null })
            throw new ArgumentException(
                "a run from bars needs its audio found or fetched first",
                nameof(direction)
            );

        if (!workingSet.HasEvidence(order.VideoId))
        {
            if (direction is null)
                return VerdictResult.Failed("no evidence to judge");

            step(new JudgeStep.DetectingWhole());
            workingSet.Prepare(order.VideoId);

            var whole = await detector.DetectWholeAsync(
                direction.Audio,
                workingSet.EvidencePathFor(order.VideoId),
                ct
            );

            if (!whole.IsSuccess)
                return VerdictResult.Failed($"detecting the whole track failed: {whole.Error}");
        }

        var folder = workingSet.Prepare(order.VideoId);
        var verdictPath = Path.Combine(folder, VerdictFileName);
        var session = Guid.NewGuid().ToString();
        var audio =
            direction?.Audio
            ?? (workingSet.TryGetSource(order.VideoId, out var held) ? held : null);
        var given = direction is null
            ? []
            : await DetectAsync(direction.Audio, folder, direction.Windows, step, ct);
        var detected = given;
        RunRecord? withheld = null;
        var resumedWithWindows = false;
        var resumedToWrite = false;

        File.Delete(verdictPath);

        var lookups = await LookUpAsync(order, folder, ct);

        step(new JudgeStep.Judging());

        var outcome = await SessionAsync(
            ["--session-id", session],
            Prompt(order, verdictPath, lookups, direction, given),
            folder,
            ct
        );

        while (true)
        {
            string prompt;

            switch (Assess(VerdictFile.Check(verdictPath), order, direction, audio, detected))
            {
                case VerdictCheck.Recordable(var run):
                    return Record(Complete(run, order, direction, audio, detected), order);

                case VerdictCheck.WindowsAsked(var windows, var run)
                    when !resumedWithWindows && audio is not null:
                    resumedWithWindows = true;
                    List<RunWindow> asked =
                    [
                        .. windows.Select(window => window with { Why = run.Reasoning }),
                    ];
                    step(new JudgeStep.JudgeAsked(asked));
                    var judged = await DetectAsync(audio, folder, asked, step, ct, given.Count);
                    detected = [.. given, .. judged];
                    withheld = Complete(run, order, direction, audio, detected);
                    prompt = WindowsPrompt(judged, verdictPath);
                    step(new JudgeStep.Judging());
                    break;

                case VerdictCheck.WindowsAsked(_, var run):
                    return Record(Complete(run, order, direction, audio, detected), order);

                case VerdictCheck.Unusable when AgentUnavailableReason(outcome) is { } reason:
                    return VerdictResult.AgentUnavailable(reason);

                case VerdictCheck.Unusable(var problem) when !resumedToWrite:
                    resumedToWrite = true;
                    prompt = $"Write the verdict file now, at {verdictPath}. Last time: {problem}.";
                    step(new JudgeStep.Resuming(problem));
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
    internal string Prompt(
        WorkOrder order,
        string verdictPath,
        IReadOnlyList<SiteLookup> lookups
    ) => Prompt(order, verdictPath, lookups, direction: null, given: []);

    private string Prompt(
        WorkOrder order,
        string verdictPath,
        IReadOnlyList<SiteLookup> lookups,
        JudgeDirection? direction,
        IReadOnlyList<DetectedWindow> given
    )
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
        lines.AddRange(PagesLines(lookups));

        if (workingSet.TryReadEvidence(order.VideoId, out var evidence))
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
                + "file, not above it. Write one section per window, labelled with the window's "
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

        var other = tab.Sections.Count(section => section.VideoId is not null);
        var synced = tab.SyncedVideoId is { } video
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

            if (section.Progression is { Length: > 0 } progression)
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

    /// <summary>The pages for this track, fetched once and then read from beside the evidence.</summary>
    private async Task<IReadOnlyList<SiteLookup>> LookUpAsync(
        WorkOrder order,
        string folder,
        CancellationToken ct
    )
    {
        var path = Path.Combine(folder, SourcesFileName);

        if (
            SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.ReadFileOrDefault(path) is
            { } cached
        )
            return cached;

        if (ArtistAndTitle(order) is not var (artist, title))
            return [];

        var lookups = await sourcePages.FetchAsync(artist, title, ct);

        SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.WriteFile(path, lookups);

        return lookups;
    }

    /// <summary>The ledger's names for a song judged before, else the row's <c>Artist - Title</c> without its bracketed tail.</summary>
    private (string Artist, string Title)? ArtistAndTitle(WorkOrder order)
    {
        if (HeldRun(order) is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title })
            return (artist, title);

        return
            order.Title.Split(" - ", 2) is [var named, var rest]
            && BracketedTail.Replace(rest, string.Empty).Trim() is { Length: > 0 } bare
            ? (named.Trim(), bare)
            : null;
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

    /// <summary>A recordable run the ledger would still refuse is unusable, so the session is told why.</summary>
    private VerdictCheck Assess(
        VerdictCheck check,
        WorkOrder order,
        JudgeDirection? direction,
        string? audio,
        IReadOnlyList<DetectedWindow> detected
    ) =>
        check is VerdictCheck.Recordable(var run)
        && RunValidation.Refusal(
            RunValidation.Normalize(Complete(run, order, direction, audio, detected))
        )
            is { } refusal
            ? new VerdictCheck.Unusable($"{refusal.Field}: {refusal.Problem}")
            : check;

    /// <summary>The run, with what crateside knows and the session cannot.</summary>
    private RunRecord Complete(
        RunRecord run,
        WorkOrder order,
        JudgeDirection? direction,
        string? audio,
        IReadOnlyList<DetectedWindow> detected
    )
    {
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
    private RunSource EarSource(TonalCentre ear) =>
        new()
        {
            Via = "the maintainer's ear",
            Kind = SourceKind.Ear,
            States = ear.Name,
            StatesKey = ear.Collapsed,
            StatesMode = ear.Mode,
            Note =
                options.Ear is EarWeight.HumanSource
                    ? "counted as a human source"
                    : "weighed as a hint",
        };

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

    [GeneratedRegex(@"(\s*[(\[][^()\[\]]*[)\]])+\s*$")]
    private static partial Regex BracketedTail { get; }

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
