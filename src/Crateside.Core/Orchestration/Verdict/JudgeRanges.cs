using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Warps;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>A range the user types or drags, in bars through the warp Ableton shows (ADR 0085), or in time without one (#116).</summary>
public static partial class JudgeRanges
{
    /// <summary><c>0:45–1:07</c>, either side also plain seconds, separated as bars are.</summary>
    public static bool TryParseTimes(string? text, out double start, out double end)
    {
        (start, end) = (0, 0);

        if (
            text is null
            || TimePattern.Match(text) is not { Success: true } match
            || !TryReadClock(match.Groups["From"].Value, out var from)
            || !TryReadClock(match.Groups["To"].Value, out var to)
            || to <= from
        )
            return false;

        (start, end) = (from, to);

        return true;
    }

    /// <summary>
    /// A typed bar range as a window in seconds, within the file; null where it does not read in
    /// the warp (<see cref="BarRanges"/>). It carries its bars only where it begins and ends on bar
    /// lines or the file's edges.
    /// </summary>
    public static RunWindow? Window(WarpGroup group, string? text, string? label) =>
        BarRanges.Read(text, group) is BarRangeReading.Read(var start, var end)
            ? new RunWindow(start, end, label, WindowAsker.User)
            {
                Bars = BarsOf(group, start, end),
            }
            : null;

    private static BarSpan? BarsOf(WarpGroup group, double start, double end) =>
        group.BeatAt(start) is { } first
        && group.BeatAt(end) is { } last
        && (start <= 0 || group.Bars.IsBarLine(first))
        && (end >= group.Seconds || group.Bars.IsBarLine(last))
            ? new BarSpan(group.Bars.BarAt(first + 1e-4), group.Bars.BarAt(last - 1e-4))
            : null;

    private static bool TryReadClock(string text, out double seconds)
    {
        seconds = 0;

        var parts = text.Split(':');

        if (
            parts is not ([_] or [_, _])
            || !double.TryParse(
                parts[^1],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var within
            )
        )
            return false;

        seconds = parts is [var minutes, _]
            ? int.Parse(minutes, CultureInfo.InvariantCulture) * 60 + within
            : within;

        return parts is [_] || within < 60;
    }

    [GeneratedRegex(
        @"^\s*(?<From>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*([–—-]|\s)\s*(?<To>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex TimePattern { get; }
}
