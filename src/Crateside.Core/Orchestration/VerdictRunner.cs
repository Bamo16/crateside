using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Turns evidence into a verdict, by handing one track's work order to a headless agent session.
/// </summary>
public sealed class VerdictRunner(
    WorkingSet workingSet,
    CratesidePaths paths,
    string executable = "claude"
)
{
    /// <summary>Tools the session may use, scoped to what the skill needs.</summary>
    private static readonly string[] AllowedTools =
    [
        "Read",
        "Write",
        "WebSearch",
        "Bash(uv run:*)",
    ];

    /// <summary>
    /// The built-in tools the session loads — distinct from <see cref="AllowedTools"/>, which
    /// controls what may run.
    /// </summary>
    private static readonly string[] BuiltInTools =
    [
        "Skill", // /detect-key itself
        "Read", // SOURCES.md
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
                "--model",
                "sonnet",
                "--strict-mcp-config",
                "--output-format",
                "json",
                "--permission-mode",
                "acceptEdits",
                // What to load, as against what to permit. See BuiltInTools.
                "--tools",
                string.Join(',', BuiltInTools),
                // Must include "project" — dropping it means /detect-key does not resolve at all.
                "--setting-sources",
                "project",
                // Tripping this leaves no ledger line, read downstream as an ordinary failure.
                "--max-budget-usd",
                "2.00",
                "--allowed-tools",
                .. AllowedTools,
            ],
            // The skill resolves relative to where the session opens, and runs uv from the
            // repo root.
            paths.KeytagRepo,
            cancellationToken
        );

        // The ledger decides success, not the exit code: a session that dies halfway leaves
        // nothing and is simply picked up again.
        if (KeyLedger.Read(paths.Ledger).HasRunFor(order.VideoId))
            return VerdictResult.Succeeded();

        // A session that never ran says nothing about the track, so it must not cost a strike.
        if (AgentUnavailableReason(outcome.StandardOutput, outcome.StandardError) is { } reason)
            return VerdictResult.AgentUnavailable(reason);

        return VerdictResult.Failed(
            outcome.Succeeded
                ? "the session finished without recording a verdict"
                : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"claude exited with {outcome.ExitCode}"
        );
    }

    /// <summary>
    /// Why the agent could not be run, read from its prose as well as its JSON, or null when the
    /// failure is about this track.
    /// </summary>
    private static string? AgentUnavailableReason(params string[] streams) =>
        streams switch
        {
            _ when Mentions(streams, "\"api_error_status\":429", "hit your limit") =>
                "the Claude allowance is spent; judging is paused until it resets",
            // Not a bare "/login", which any searched-up URL could carry.
            _ when Mentions(streams, "not logged in", "please run /login") =>
                "Claude Code is not logged in; judging is paused until you run /login",
            _ => null,
        };

    private static bool Mentions(string[] streams, params string[] phrases) =>
        streams.Any(stream =>
            phrases.Any(phrase => stream.Contains(phrase, StringComparison.OrdinalIgnoreCase))
        );

    /// <summary>
    /// The work order, rendered for the session. Names the evidence path even when embedding it,
    /// since the recorder takes it as <c>--machine</c>.
    /// </summary>
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

        lines.AddRange([
            string.Empty,
            "Record the run when you have a verdict. source_url is required.",
            string.Empty,
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
