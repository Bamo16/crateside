using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Warps;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>A range the user types or drags, in bars through the warp Ableton shows, or in time without one (#116).</summary>
public static partial class JudgeRanges
{
    /// <summary><c>33–48</c>, <c>33-48</c> or <c>33 48</c>.</summary>
    public static bool TryParseBars(string? text, [NotNullWhen(true)] out BarSpan? bars)
    {
        bars =
            text is not null
            && BarPattern.Match(text) is { Success: true } match
            && int.Parse(match.Groups["First"].Value, CultureInfo.InvariantCulture)
                is >= 1
                    and var first
            && int.Parse(match.Groups["Last"].Value, CultureInfo.InvariantCulture) is var last
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
        && group.SecondsAt(BeatOfBar(group, bars.First)) is { } start
        && group.SecondsAt(BeatOfBar(group, bars.Last + 1)) is { } end
        && end > start
            ? new RunWindow(Math.Max(0, start), end, label, WindowAsker.User) { Bars = bars }
            : null;

    /// <summary>The whole bars a stretch in seconds covers, for drawing a window given in time; null without a warp.</summary>
    public static BarSpan? BarsOf(WarpGroup group, double start, double end) =>
        group.Lender is not null
        && group.BeatAt(start) is { } first
        && group.BeatAt(end) is { } last
            ? new BarSpan(Math.Max(1, BarAt(group, first)), Math.Max(1, BarAt(group, last - 0.001)))
            : null;

    /// <summary>The bar a beat falls in, as Live counts from the clip start.</summary>
    public static int BarAt(WarpGroup group, double beat) =>
        (int)Math.Floor((beat - group.ClipStartBeat) / group.BeatsPerBar) + 1;

    public static double BeatOfBar(WarpGroup group, int bar) =>
        group.ClipStartBeat + (bar - 1) * group.BeatsPerBar;

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
        @"^\s*(?<First>\d{1,4})\s*([–—-]|\s)\s*(?<Last>\d{1,4})\s*$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex BarPattern { get; }

    [GeneratedRegex(
        @"^\s*(?<From>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*([–—-]|\s)\s*(?<To>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex TimePattern { get; }
}
