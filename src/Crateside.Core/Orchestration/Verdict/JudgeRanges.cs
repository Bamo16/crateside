using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Warps;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>A range the user types or drags, in bars through the warp Ableton shows, or in time without one (#116).</summary>
public static partial class JudgeRanges
{
    /// <summary><c>33–48</c>, <c>33-48</c> or <c>33 48</c>; <c>-4–2</c> for bars before beat 0, there being no bar 0.</summary>
    public static bool TryParseBars(string? text, [NotNullWhen(true)] out BarSpan? bars)
    {
        bars =
            text is not null
            && BarPattern.Match(text) is { Success: true } match
            && int.Parse(match.Groups["First"].Value, CultureInfo.InvariantCulture)
                is not 0
                    and var first
            && int.Parse(match.Groups["Last"].Value, CultureInfo.InvariantCulture)
                is not 0
                    and var last
            && last >= first
                ? new BarSpan(first, last)
                : null;

        return bars is not null;
    }

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

    /// <summary>The bars as a window in seconds, from the start of the first to the end of the last; null where the warp cannot say.</summary>
    public static RunWindow? Window(WarpGroup group, BarSpan bars, string? label) =>
        group.Lender is not null
        && group.SecondsAt(group.Bars.StartOf(bars.First)) is { } start
        && group.SecondsAt(group.Bars.EndOf(bars.Last)) is { } end
        && end > start
            ? new RunWindow(Math.Max(0, start), end, label, WindowAsker.User) { Bars = bars }
            : null;

    /// <summary>The whole bars a stretch in seconds covers, for drawing a window given in time; null without a warp.</summary>
    public static BarSpan? BarsOf(WarpGroup group, double start, double end) =>
        group.Lender is not null
        && group.BeatAt(start) is { } first
        && group.BeatAt(end) is { } last
            ? new BarSpan(group.Bars.BarAt(first), group.Bars.BarAt(last - 0.001))
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
        @"^\s*(?<First>-?\d{1,4})(\s*[–—]\s*|-\s*|\s+-\s+|\s+)(?<Last>-?\d{1,4})\s*$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex BarPattern { get; }

    [GeneratedRegex(
        @"^\s*(?<From>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*([–—-]|\s)\s*(?<To>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex TimePattern { get; }
}
