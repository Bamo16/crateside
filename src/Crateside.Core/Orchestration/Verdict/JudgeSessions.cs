using System.Text.Json.Nodes;
using Crateside.Core.Orchestration.Loop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Launches the headless Claude Code sessions a judge runs in, and tallies what they spend.</summary>
public sealed class JudgeSessions(
    IProcessRunner processes,
    IOptions<JudgeOptions> options,
    ILogger<JudgeSessions> log,
    string fileName = "claude"
)
{
    private static readonly string[] Tools = ["Read", "Write", "WebSearch", "WebFetch"];

    private readonly JudgeOptions _options = options.Value;

    internal string FileName { get; } = fileName;

    internal async Task<ProcessOutcome> RunAsync(
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
                FileName,
                [
                    "-p",
                    .. session,
                    "--model",
                    _options.Model,
                    "--output-format",
                    "json",
                    "--append-system-prompt",
                    JudgePrompt.Instructions,
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
        tally.CostUsd += result["total_cost_usd"]?.GetValue<double>() ?? 0;

        log.LogInformation(
            "Judge session for {TrackKey} on {Model}: {Turns} turns, {Seconds:0} s, ${CostUsd:0.00}",
            order.TrackKey,
            _options.Model,
            result["num_turns"]?.GetValue<int>(),
            result["duration_ms"]?.GetValue<double>() / 1000,
            result["total_cost_usd"]?.GetValue<double>()
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
}

/// <summary>What a run's judge sessions spent, added up as they end.</summary>
internal sealed class SessionTally
{
    public int Turns { get; set; }

    public double Seconds { get; set; }

    /// <summary>Claude Code's own estimate; zero when the endpoint isn't one it can price.</summary>
    public double CostUsd { get; set; }
}
