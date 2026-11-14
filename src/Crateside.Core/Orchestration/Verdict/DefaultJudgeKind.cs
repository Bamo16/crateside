using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Chooses the judge when none is set: Claude Code if it is installed, else none (ADR 0076).</summary>
public sealed class DefaultJudgeKind(IClaudeInstall claude) : IPostConfigureOptions<JudgeOptions>
{
    public void PostConfigure(string? name, JudgeOptions options) =>
        options.Kind ??= claude.IsInstalled ? JudgeKind.ClaudeCode : JudgeKind.None;
}

/// <summary>Whether the <c>claude</c> executable the judge starts can be found.</summary>
public interface IClaudeInstall
{
    bool IsInstalled { get; }
}

/// <summary>Looks for <c>claude</c> on PATH, where the judge's process will look for it.</summary>
public sealed class ClaudeOnPath : IClaudeInstall
{
    public bool IsInstalled => FfmpegBuild.Located("claude") is not null;
}
