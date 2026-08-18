using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Turns evidence into a <strong>verdict</strong>, by handing one track's work order to a
/// headless agent session.
/// </summary>
/// <remarks>
/// <para>
/// The only stage that needs an agent, because weighing evidence and going to look for human
/// corroboration is the only part of the workflow that genuinely does. Everything else it used
/// to do — choosing a URL, downloading, running the detectors, separating, routing — belongs to
/// crateside now, so a run is evidence in and a verdict out.
/// </para>
/// <para>
/// <strong>A human never transports the instruction.</strong> ADR 0003 (keytag) was right that
/// the prompt is the artifact and wrong that a person carries it: crateside composes it and
/// hands it straight to the session.
/// </para>
/// </remarks>
public sealed class VerdictRunner(
    WorkingSet workingSet,
    CratesidePaths paths,
    string executable = "claude"
)
{
    /// <summary>
    /// Tools the session may use. Everything the skill needs and nothing more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Bash is narrowed to <c>uv run</c>.</strong> More than one separation at a time is
    /// the single thing that makes this machine unusable, and <c>SKILL.md</c> forbidding it is
    /// prose — which an agent can reason its way past, and which no longer applies the moment
    /// somebody edits that file. A scoped tool list is the same kind of guard as
    /// <see cref="SeparationInterlock"/>: structural rather than remembered.
    /// </para>
    /// <para>
    /// <c>WebSearch</c> and not <c>WebFetch</c>, because the human sources all 403 fetching and
    /// the key is in the snippet — so the fetch tool would only ever cost time.
    /// </para>
    /// </remarks>
    private static readonly string[] AllowedTools =
    [
        "Read",
        "Write",
        "WebSearch",
        "Bash(uv run:*)",
    ];

    /// <summary>
    /// The built-in tools the session <em>loads</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A different list from <see cref="AllowedTools"/>, and confusing the two cost real
    /// money.</strong> <c>--allowed-tools</c> is a permission list: it decides what may run, not
    /// what exists. Every built-in was still being handed to the session, which is enough of them
    /// that the rarely-used ones — <c>WebSearch</c> among them — sit behind <c>ToolSearch</c>. So
    /// both measured tracks spent a whole round trip looking up a tool they had already been
    /// granted. <c>--tools</c> is the list that actually decides; naming it loads
    /// <c>WebSearch</c> directly and drops ~6.9k of tool definitions from every turn.
    /// </para>
    /// <para>
    /// The permission list still governs what these may do — <c>Bash</c> here and
    /// <c>Bash(uv run:*)</c> above is the same structural guard as before, not a widening.
    /// </para>
    /// </remarks>
    private static readonly string[] BuiltInTools =
    [
        "Skill", // /detect-key itself
        "Read", // SOURCES.md; the evidence arrives in the prompt now
        "Write", // the machine layer the recorder reads back
        "WebSearch",
        "Bash",
    ];

    /// <summary>Judges one track, and reports what the ledger says afterwards.</summary>
    public async Task<VerdictResult> RunAsync(
        WorkOrder order,
        CancellationToken cancellationToken = default
    )
    {
        if (!workingSet.HasEvidence(order.VideoId))
            return VerdictResult.Failed("no evidence to judge");

        var outcome = await ExternalProcess.RunAsync(
            executable,
            [
                "-p",
                Prompt(order),
                // Judgment is reading a JSON file, weighing three numbers against each other,
                // usually one search, and writing a record. Left to the default this ran on Opus
                // and spent 180k tokens on a single track; the work does not need it, and the
                // user's own pre-crateside habit was a Sonnet agent over a batch.
                "--model",
                "sonnet",
                // The skill needs no MCP server, and the ones configured for interactive use --
                // Supabase, Vercel, Drive -- would put their whole tool surface into every
                // judging session for nothing.
                "--strict-mcp-config",
                "--output-format",
                "json",
                "--permission-mode",
                "acceptEdits",
                // What to load, as against what to permit. See BuiltInTools.
                "--tools",
                string.Join(',', BuiltInTools),
                // The skill is a project setting of the keytag repo, so that source has to stay;
                // the user's global ones only ever contributed a listing of every skill and agent
                // they have installed, none of which judging can use. Worth 4.7k a turn, and
                // "" -- dropping the project source too -- is not an option: /detect-key then
                // does not resolve at all, which was measured rather than assumed.
                "--setting-sources",
                "project",
                // A backstop against a session that spirals rather than stops. Measured tracks
                // cost $0.44 and $0.54 before this change, so this only ever catches a runaway.
                // Tripping it leaves no ledger line, which the caller already reads as an
                // ordinary failure -- which is what a runaway should be.
                "--max-budget-usd",
                "2.00",
                "--allowed-tools",
                .. AllowedTools,
            ],
            // The session finds the detect-key skill relative to where it opens, and the skill
            // runs uv from the repo root.
            paths.KeytagRepo,
            cancellationToken
        );

        // Reality, not a claim. A recorded line is the artifact this stage exists to produce,
        // and it is the exact negation of the selector's own predicate -- so a session that
        // died halfway leaves nothing and is simply picked up again. The exit code is kept for
        // the message and the strike count and decides nothing, which is also what keeps
        // crateside out of the business of parsing agent output.
        if (KeyLedger.Read(paths.Ledger).HasRunFor(order.VideoId))
            return VerdictResult.Succeeded();

        // Running out of quota says nothing about this track, so it must not be charged for it.
        // Left as an ordinary failure it burned all three strikes in about a second -- the
        // second and third attempts returned instantly, cost nothing, and achieved nothing
        // except to kill the track for good, so that when quota returned it was already out.
        if (IsQuotaExhausted(outcome.StandardOutput, outcome.StandardError))
            return VerdictResult.QuotaExhausted();

        return VerdictResult.Failed(
            outcome.Succeeded
                ? "the session finished without recording a verdict"
                : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"claude exited with {outcome.ExitCode}"
        );
    }

    /// <summary>
    /// Whether the session stopped because the allowance is spent rather than because anything
    /// about this track is wrong.
    /// </summary>
    /// <remarks>
    /// Read out of the session's own JSON, which reports <c>api_error_status: 429</c> alongside a
    /// human sentence naming the reset time. Either is enough: the status alone is unambiguous,
    /// and a session that stops before emitting one still says it in prose.
    /// </remarks>
    private static bool IsQuotaExhausted(params string[] streams) =>
        streams.Any(s =>
            s.Contains("\"api_error_status\":429", StringComparison.Ordinal)
            || s.Contains("hit your limit", StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// The work order, rendered for the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every line is something the skill would otherwise have to decide or go and find. The
    /// audio is named so a diluted whole-file read can be re-run over a window, and named
    /// read-only so it does not read as licence to start again.
    /// </para>
    /// <para>
    /// <strong>The evidence is carried, not pointed at.</strong> The path is still named,
    /// because the recorder takes it as its <c>--machine</c> argument, but the session no
    /// longer has to spend a turn reading it.
    /// </para>
    /// </remarks>
    internal string Prompt(WorkOrder order)
    {
        List<string> lines =
        [
            "/detect-key",
            string.Empty,
            "Judge one track from evidence already gathered for it. Do not acquire audio.",
            string.Empty,
            $"- title: {order.Title}",
            $"- source_url: {order.SourceUrl}",
            $"- evidence: {workingSet.EvidencePathFor(order.VideoId)}",
        ];

        if (workingSet.TryGetSource(order.VideoId, out var audio))
            lines.Add($"- audio (read-only, for a --segment re-run only): {audio}");

        if (order.HasVocals is false)
            lines.Add(
                "- has_vocals: false — the user states this is an instrumental release. "
                    + "Record it; do not re-derive it."
            );

        lines.AddRange([
            string.Empty,
            "Record the run when you have a verdict. source_url is required.",
            string.Empty,
            // The session opens in the keytag repo and reads its CLAUDE.md -- "commit directly
            // to master... do not ask each time" -- as addressed to itself. One track spent
            // three turns and ~150k tokens trying to commit the ledger, and all three were
            // blocked by the permission list, which is the guard working and not a reason to
            // pay for the attempts. Naming who the instruction is for beats forbidding it flat,
            // because the session can otherwise reason its way back to obeying the file.
            "Do not run git. keytag's instruction to commit and push is addressed to somebody "
                + "working on keytag, not to a judging session; crateside owns what happens to "
                + "the ledger afterwards.",
        ]);

        if (workingSet.TryReadEvidence(order.VideoId, out var evidence))
            lines.AddRange([
                string.Empty,
                "The evidence itself, so you do not have to read it:",
                string.Empty,
                "```json",
                evidence.TrimEnd(),
                "```",
            ]);

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>How one verdict run turned out.</summary>
public sealed record VerdictResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// The allowance is spent. Not a failure of the track, and not something retrying fixes —
    /// the stage has to stop until it resets.
    /// </summary>
    public bool IsQuotaExhausted { get; init; }

    public string? Error { get; init; }

    public static VerdictResult Succeeded() => new() { IsSuccess = true };

    public static VerdictResult Failed(string error) => new() { IsSuccess = false, Error = error };

    public static VerdictResult QuotaExhausted() =>
        new()
        {
            IsSuccess = false,
            IsQuotaExhausted = true,
            Error = "the Claude allowance is spent; judging is paused until it resets",
        };
}
