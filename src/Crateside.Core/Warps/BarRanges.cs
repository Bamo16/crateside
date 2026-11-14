using System.Globalization;
using System.Text.RegularExpressions;

namespace Crateside.Core.Warps;

/// <summary>
/// Bar ranges read the way Live's Time Selection reads: the end is a position, where the
/// selection stops, so <c>65–66</c> is bar 65 alone (ADR 0085).
/// </summary>
public static partial class BarRanges
{
    public const string EmptyMessage =
        "The end is where the selection stops, as in Live: 65–66 is bar 65. A range is a sixteenth long at least.";

    // A position read back from seconds lands a hair either side of its line.
    private const double Snap = 1e-6;
    private const double Sixteenth = 0.25;

    /// <summary>
    /// <c>33–49</c>, <c>33-49</c> or <c>33 49</c>, each side a bar or Live's <c>bar.beat.sixteenth</c>
    /// (<c>155.1.1–156.3.1</c>); <c>-4–2</c> for bars before beat 0. Either side may be left open:
    /// <c>start-105</c>, <c>106-end</c> or <c>106-</c>.
    /// </summary>
    public static BarRangeReading Read(string? text, WarpGroup group)
    {
        if (
            group.Lender is null
            || text is null
            || RangePattern.Match(text) is not { Success: true } match
        )
            return new BarRangeReading.Unreadable();

        // "33" alone is not a range; "33-" is, open at the end.
        if (
            !match.Groups["Last"].Success
            && match.Groups["Separator"].Value.IndexOfAny(['-', '–', '—']) < 0
        )
            return new BarRangeReading.Unreadable();

        var first = BeatOf(group.Bars, match, "First");
        var last = BeatOf(group.Bars, match, "Last");
        var isOpenStart = match
            .Groups["First"]
            .Value.Equals("start", StringComparison.OrdinalIgnoreCase);
        var isOpenEnd =
            !match.Groups["Last"].Success
            || match.Groups["Last"].Value.Equals("end", StringComparison.OrdinalIgnoreCase);

        if ((!isOpenStart && first is null) || (!isOpenEnd && last is null))
            return new BarRangeReading.Unreadable();

        if (first is { } from && last is { } to)
        {
            if (to - from < -Snap)
                return new BarRangeReading.Unreadable();

            if (to - from < Sixteenth - Snap)
                return new BarRangeReading.TooShort();
        }

        double? start = isOpenStart ? 0 : group.SecondsAt(first!.Value);
        double? end = isOpenEnd
            ? (group.Seconds > 0 ? group.Seconds : null)
            : group.SecondsAt(last!.Value);

        if (start is not { } startSeconds || end is not { } endSeconds)
            return new BarRangeReading.Unreadable();

        startSeconds = Math.Max(startSeconds, 0);
        endSeconds = group.Seconds > 0 ? Math.Min(endSeconds, group.Seconds) : endSeconds;

        return endSeconds > startSeconds
            ? new BarRangeReading.Read(startSeconds, endSeconds)
            : new BarRangeReading.Unreadable();
    }

    /// <summary>The stretch as Live's positions, <c>183–190</c> or <c>155.1.1–156.3.1</c>; null without a warp.</summary>
    public static string? Show(WarpGroup group, double startSeconds, double endSeconds) =>
        group.Lender is not null
        && group.BeatAt(startSeconds) is { } first
        && group.BeatAt(endSeconds) is { } last
            ? $"{At(group.Bars, first)}–{At(group.Bars, last)}"
            : null;

    /// <summary>A position as Live's status bar writes it, to the nearest sixteenth.</summary>
    public static string At(BarRuler bars, double beat) => bars.Position(Math.Round(beat * 4) / 4);

    private static double? BeatOf(BarRuler bars, Match match, string side)
    {
        if (
            !int.TryParse(
                match.Groups[$"{side}Bar"].Value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var bar
            ) || bar is 0
        )
            return null;

        var unit = 4.0 / bars.Denominator;
        var beat = Count(match.Groups[$"{side}Beat"]);
        var sixteenth = Count(match.Groups[$"{side}Sixteenth"]);

        return
            beat is < 1 || beat > bars.Numerator || sixteenth is < 1 || sixteenth > unit / Sixteenth
            ? null
            : bars.StartOf(bar) + ((beat ?? 1) - 1) * unit + ((sixteenth ?? 1) - 1) * Sixteenth;
    }

    private static int? Count(Group group) =>
        group.Success
            ? int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;

    [GeneratedRegex(
        @"
        ^\s*
        (?<First>start|(?<FirstBar>-?\d{1,4})(\.(?<FirstBeat>\d{1,2}))?(\.(?<FirstSixteenth>\d{1,2}))?)
        (?<Separator>\s*[–—]\s*|-\s*|\s+-\s+|\s+)
        (?<Last>end|(?<LastBar>-?\d{1,4})(\.(?<LastBeat>\d{1,2}))?(\.(?<LastSixteenth>\d{1,2}))?)?
        \s*$
        ",
        RegexOptions.ExplicitCapture
            | RegexOptions.IgnorePatternWhitespace
            | RegexOptions.IgnoreCase
    )]
    private static partial Regex RangePattern { get; }
}

/// <summary>What a typed bar range came to.</summary>
public abstract record BarRangeReading
{
    private BarRangeReading() { }

    /// <summary>The range in seconds of the file, clamped to it.</summary>
    public sealed record Read(double Start, double End) : BarRangeReading;

    /// <summary>Not a range in the warp, or out of order.</summary>
    public sealed record Unreadable : BarRangeReading;

    /// <summary>A range under a sixteenth long, which includes one that ends where it starts.</summary>
    public sealed record TooShort : BarRangeReading;
}
