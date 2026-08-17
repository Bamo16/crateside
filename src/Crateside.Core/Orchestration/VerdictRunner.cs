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
                "--output-format",
                "json",
                "--permission-mode",
                "acceptEdits",
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

        return VerdictResult.Failed(
            outcome.Succeeded
                ? "the session finished without recording a verdict"
                : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"claude exited with {outcome.ExitCode}"
        );
    }

    /// <summary>
    /// The work order, rendered for the session.
    /// </summary>
    /// <remarks>
    /// Every line is something the skill would otherwise have to decide or go and find. The
    /// audio is named so a diluted whole-file read can be re-run over a window, and named
    /// read-only so it does not read as licence to start again.
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
        ]);

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>How one verdict run turned out.</summary>
public sealed record VerdictResult
{
    public required bool IsSuccess { get; init; }
    public string? Error { get; init; }

    public static VerdictResult Succeeded() => new() { IsSuccess = true };

    public static VerdictResult Failed(string error) => new() { IsSuccess = false, Error = error };
}
