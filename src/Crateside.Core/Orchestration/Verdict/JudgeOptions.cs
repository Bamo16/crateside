namespace Crateside.Core.Orchestration.Verdict;

/// <summary>How the judge is run: its model, its endpoint, and how much the user's ear counts (docs/judge.md).</summary>
public sealed class JudgeOptions
{
    public const string SectionName = "Judge";

    /// <summary>Which judge runs; null until <see cref="DefaultJudgeKind"/> has chosen one from what is installed.</summary>
    public JudgeKind? Kind { get; set; }

    /// <summary>What <c>claude --model</c> is given: an alias or a full model name.</summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>Variables set for the judge process only, such as <c>ANTHROPIC_BASE_URL</c>; empty means Claude Code's own login.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    public EarWeight Ear { get; set; } = EarWeight.Hint;

    /// <summary>Whether any judge is set up; without one, crateside rules where it can and the person decides the rest (ADR 0076).</summary>
    public bool IsConfigured => Kind is not JudgeKind.None;

    /// <summary>Whether the judge runs on Claude Code's own login, and so spends the weekly Claude limit (ADR 0067).</summary>
    public bool SpendsClaudeQuota =>
        IsConfigured
        && !Environment.ContainsKey("ANTHROPIC_BASE_URL")
        && !Environment.ContainsKey("ANTHROPIC_API_KEY")
        && !Environment.ContainsKey("ANTHROPIC_AUTH_TOKEN");
}

/// <summary>Which judge runs verdicts that the rules leave open (ADR 0076).</summary>
public enum JudgeKind
{
    /// <summary>A headless Claude Code session, on its own login or the endpoint in <see cref="JudgeOptions.Environment"/>.</summary>
    ClaudeCode,

    /// <summary>No judge: the person decides what the rules leave.</summary>
    None,
}

/// <summary>How much the user's own reading of a key counts with the judge (ADR 0055).</summary>
public enum EarWeight
{
    /// <summary>Weighed, but never raises confidence by itself.</summary>
    Hint,

    /// <summary>Counted like an authoritative source.</summary>
    HumanSource,
}
