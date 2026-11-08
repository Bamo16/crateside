using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>The words an endpoint gave for refusing a session, read from Claude Code's result JSON (ADR 0070).</summary>
internal static partial class EndpointError
{
    /// <summary>The error text of the session's result, or null when it carried none.</summary>
    public static string? ResultOf(string standardOutput) =>
        ResultPattern.Match(standardOutput ?? string.Empty).Groups["Message"]
            is { Success: true, Value: var raw }
            ? Unescaped(raw)
            : null;

    /// <summary>
    /// What the endpoint itself said once Claude Code's lead-in ("Failed to authenticate.",
    /// "API Error: 401") and any JSON wrapper around its message are taken off; null when it said nothing.
    /// </summary>
    public static string? WordsOf(string? result)
    {
        if (result is not { Length: > 0 })
            return null;

        var words = Wrapper.Replace(result, string.Empty).Trim();

        if (JsonMessage.Match(words).Groups["Message"] is { Success: true, Value: var message })
            words = Unescaped(message).Trim();

        return words is { Length: > 0 } ? words : null;
    }

    private static string Unescaped(string raw) =>
        JsonSerializer.Deserialize<string>($"\"{raw}\"") ?? raw;

    [GeneratedRegex(
        @"""result"":\s*""(?<Message>(?:[^""\\]|\\.)*)""",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex ResultPattern { get; }

    [GeneratedRegex(
        @"^\s*(Failed to authenticate\.?\s*)?(API Error:?\s*)?(\d{3}\b)?[\s:·-]*",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
    )]
    private static partial Regex Wrapper { get; }

    [GeneratedRegex(
        @"""message""\s*:\s*""(?<Message>(?:[^""\\]|\\.)*)""",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex JsonMessage { get; }
}
