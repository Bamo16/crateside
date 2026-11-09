using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Loop;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Tells a session that could not be served from one that failed on its track (ADR 0070).</summary>
public sealed partial class JudgeAvailability(IOptions<JudgeOptions> options)
{
    private const int MaxRetryAfterSeconds = 3600;

    private readonly JudgeOptions _options = options.Value;

    /// <summary>
    /// Why no session could be served, read from its prose as well as its JSON, or null when the
    /// failure is about this track (ADR 0070).
    /// </summary>
    internal VerdictResult? Unavailable(ProcessOutcome outcome)
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

        if (status is 401 or 403 || Mentions(streams, "Failed to authenticate"))
            return Refused(
                EndpointError.WordsOf(EndpointError.ResultOf(outcome.StandardOutput)) switch
                {
                    // "refused its key" only where the endpoint speaks of a key.
                    { } words when words.Contains("key", StringComparison.OrdinalIgnoreCase) =>
                        $"the judge's endpoint refused its key: {words}",
                    { } words => $"the judge's endpoint refused the session: {words}",
                    null => "the judge's endpoint refused the session",
                }
            );

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

    /// <summary>
    /// The endpoint's own error from the session's result, which Claude Code's stderr can misname
    /// (any 422 prints as "unrecognized_model").
    /// </summary>
    internal static string? ApiError(ProcessOutcome outcome) =>
        EndpointError.ResultOf(outcome.StandardOutput) is { } result
        && result.StartsWith("API Error:", StringComparison.Ordinal)
            ? result
            : null;

    [GeneratedRegex(
        @"\bpause for (?<Seconds>\d+)\s+(more\s+)?seconds",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture
    )]
    private static partial Regex StatedPause { get; }
}
