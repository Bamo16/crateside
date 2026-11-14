using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Orchestration.Verdict;

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

    /// <summary>The numbers <see cref="Summary"/> would be worded from, when the judge ran; the job record stores these.</summary>
    public JobFacts? Facts { get; init; }

    public static VerdictResult Succeeded(string? summary = null, JobFacts? facts = null) =>
        new()
        {
            IsSuccess = true,
            Summary = summary,
            Facts = facts,
        };

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
