using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ableton;

/// <summary>Which set Ableton has open, read off its window title (ADR 0040).</summary>
public static partial class AbletonSession
{
    private const string ProcessPrefix = "Ableton Live";

    /// <summary>The set's name, an asterisk while it has unsaved changes, then the product.</summary>
    [GeneratedRegex(@"^(?<Set>.+?)\*? - Ableton Live\b")]
    private static partial Regex Title { get; }

    public static OpenSet Read()
    {
        var titles = Process
            .GetProcesses()
            .Where(process =>
                process.ProcessName.StartsWith(ProcessPrefix, StringComparison.OrdinalIgnoreCase)
            )
            .Select(process => process.MainWindowTitle)
            .ToList();

        return titles switch
        {
            [] => OpenSet.NotRunning,
            _ => titles.Select(SetFromTitle).OfType<string>().FirstOrDefault() is { } set
                ? new OpenSet(IsRunning: true, set)
                : OpenSet.Unknown,
        };
    }

    /// <summary>The set a window title names, or null when it names none (a dialog, the splash).</summary>
    public static string? SetFromTitle(string title) =>
        Title.Match(title) is { Success: true } match ? match.Groups["Set"].Value : null;
}

/// <summary>Whether Ableton is running, and the set it has open when its window says.</summary>
public sealed record OpenSet(bool IsRunning, string? Set)
{
    public static OpenSet NotRunning { get; } = new(false, null);

    /// <summary>Running, with a window that names no set: a dialog, or still starting.</summary>
    public static OpenSet Unknown { get; } = new(true, null);
}
