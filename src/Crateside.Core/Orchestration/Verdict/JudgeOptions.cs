namespace Crateside.Core.Orchestration.Verdict;

/// <summary>How the judge is run: its model, its endpoint, and how much the user's ear counts (docs/judge.md).</summary>
public sealed class JudgeOptions
{
    public const string SectionName = "Judge";

    /// <summary>What <c>claude --model</c> is given: an alias or a full model name.</summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>Variables set for the judge process only, such as <c>ANTHROPIC_BASE_URL</c>; empty means Claude Code's own login.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    public EarWeight Ear { get; set; } = EarWeight.Hint;

    /// <summary>Whether the judge runs on Claude Code's own login, and so spends the weekly Claude limit (ADR 0067).</summary>
    public bool SpendsClaudeQuota =>
        !Environment.ContainsKey("ANTHROPIC_BASE_URL")
        && !Environment.ContainsKey("ANTHROPIC_API_KEY")
        && !Environment.ContainsKey("ANTHROPIC_AUTH_TOKEN");
}

/// <summary>How much the user's own reading of a key counts with the judge (ADR 0055).</summary>
public enum EarWeight
{
    /// <summary>Weighed, but never raises confidence by itself.</summary>
    Hint,

    /// <summary>Counted like an authoritative source.</summary>
    HumanSource,
}
