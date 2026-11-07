using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Warps;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>A range the user types or drags, in bars through the warp Ableton shows, or in time without one (#116).</summary>
public static partial class JudgeRanges
{
    /// <summary>
    /// <c>33–48</c>, <c>33-48</c> or <c>33 48</c>; <c>-4–2</c> for bars before beat 0, there being no bar 0.
    /// Either side may be left open: <c>start-105</c>, <c>106-end</c> or <c>106-</c>.
    /// </summary>
    public static bool TryParseBars(string? text, [NotNullWhen(true)] out BarRange? range)
    {
        range = null;

        if (text is null || BarPattern.Match(text) is not { Success: true } match)
            return false;

        var last = match.Groups["Last"];

        // "33" alone is not a range; "33-" is, open at the end.
        if (!last.Success && match.Groups["Separator"].Value.IndexOfAny(['-', '–', '—']) < 0)
            return false;

        var first = ReadBar(match.Groups["First"].Value);
        var end = last.Success ? ReadBar(last.Value) : null;

        if (first is 0 || end is 0 || first > end)
            return false;

        range = new BarRange(first, end);

        return true;
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

    /// <summary>The range with open sides filled in from the start and end of the file, and clamped to it; null where the warp cannot say.</summary>
    public static BarSpan? Resolve(WarpGroup group, BarRange range)
    {
        if (group.Lender is null || group.BeatAt(0) is not { } startBeat)
            return null;

        var firstBar = group.Bars.BarAt(startBeat);
        int? lastBar =
            group.Seconds > 0 && group.BeatAt(group.Seconds) is { } endBeat
                ? group.Bars.BarAt(endBeat - 0.001)
                : null;

        var first = Math.Max(range.First ?? firstBar, firstBar);

        return (range.Last ?? lastBar, lastBar) switch
        {
            (null, _) => null,
            ({ } last, { } clamp) when Math.Min(last, clamp) >= first => new BarSpan(
                first,
                Math.Min(last, clamp)
            ),
            ({ } last, null) when last >= first => new BarSpan(first, last),
            _ => null,
        };
    }

    /// <summary>The range as a window in seconds, from the start of the first bar to the end of the last, within the file; null where the warp cannot say.</summary>
    public static RunWindow? Window(WarpGroup group, BarRange range, string? label) =>
        Resolve(group, range) is { } bars
        && group.SecondsAt(group.Bars.StartOf(bars.First)) is { } start
        && group.SecondsAt(group.Bars.EndOf(bars.Last)) is { } end
        && (group.Seconds is 0 ? end : Math.Min(end, group.Seconds)) is var clamped
        && clamped > Math.Max(0, start)
            ? new RunWindow(Math.Max(0, start), clamped, label, WindowAsker.User) { Bars = bars }
            : null;

    /// <summary>The whole bars a stretch in seconds covers, for drawing a window given in time; null without a warp.</summary>
    public static BarSpan? BarsOf(WarpGroup group, double start, double end) =>
        group.Lender is not null
        && group.BeatAt(start) is { } first
        && group.BeatAt(end) is { } last
            ? new BarSpan(group.Bars.BarAt(first), group.Bars.BarAt(last - 0.001))
            : null;

    private static int? ReadBar(string text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bar)
            ? bar
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
        @"^\s*(?<First>-?\d{1,4}|start)(?<Separator>\s*[–—]\s*|-\s*|\s+-\s+|\s+)(?<Last>-?\d{1,4}|end)?\s*$",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
    )]
    private static partial Regex BarPattern { get; }

    [GeneratedRegex(
        @"^\s*(?<From>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*([–—-]|\s)\s*(?<To>\d{1,3}(:\d{1,2}(\.\d+)?)?)\s*$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex TimePattern { get; }
}
