using System.Text.Json;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Naming;

/// <summary>
/// Asks StemForge what it will call a track's files, before any work runs. Never rebuild either
/// name from parts (ADR 0014).
/// </summary>
public sealed class NameResolver(IProcessRunner processes, string stemforge = "stemforge-cli")
{
    /// <summary>
    /// What the files will be called, or null when the upload could not be resolved. Never
    /// throws: a private, removed or unavailable video is an answer, not a fault.
    /// </summary>
    public async Task<ResolvedName?> ResolveAsync(string sourceUrl, CancellationToken ct = default)
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await processes.RunAsync(
                new ProcessRequest(stemforge, ["download", sourceUrl, "--list-formats", "--json"]),
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        // A non-zero exit means some or all inputs failed, which the JSON itself describes — so
        // it is parsed either way rather than trusted to the exit code.
        return
            Parse(outcome.StandardOutput)
                is [{ Succeeded: true, DisplayTitle: { Length: > 0 } display } row, ..]
            ? new ResolvedName
            {
                DisplayTitle = display,
                BaseName = row.BaseName is { Length: > 0 } file ? file : display,
            }
            : null;
    }

    private static List<FormatListOutput>? Parse(string stdout)
    {
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize(
                stdout[start..],
                StemForgeJsonContext.Default.ListFormatListOutput
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What StemForge will call one track's files.</summary>
public sealed record ResolvedName
{
    /// <summary>The human name — what a row shows, and what a rename starts from.</summary>
    public required string DisplayTitle { get; init; }

    /// <summary>
    /// The filename the source is given, without extension.
    /// </summary>
    public required string BaseName { get; init; }
}

/// <summary>One element of <c>download --list-formats --json</c>.</summary>
internal sealed record FormatListOutput
{
    public bool Succeeded { get; init; }

    public string? DisplayTitle { get; init; }

    public string? BaseName { get; init; }

    public string? Error { get; init; }
}
