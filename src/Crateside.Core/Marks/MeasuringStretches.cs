using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;

namespace Crateside.Core.Marks;

/// <summary>Reads a stretch typed in Live's bars (<c>183–190</c>, <c>183.3–190.2</c>) or, without a warp, in time (<c>2:22–3:39</c>) (ADR 0085).</summary>
public static partial class MeasuringStretches
{
    // A bar line read back from seconds lands a hair either side of it.
    private const double Snap = 1e-4;

    /// <summary>
    /// Bars run from the warp's beat 0 as Live numbers them (ADR 0072). A bare end bar is the end
    /// of that bar; an end with a beat is that exact position. <c>start</c> and <c>end</c> are the
    /// file's edges.
    /// </summary>
    public static bool TryRead(
        string? text,
        string reason,
        WarpGroup group,
        [NotNullWhen(true)] out MeasuringStretch? stretch
    )
    {
        stretch = null;

        var read = group.Lender is null ? ReadTimes(text) : ReadBars(text, group);

        if (read is not var (start, end) || end <= start)
            return false;

        stretch = new MeasuringStretch
        {
            StartSeconds = start,
            EndSeconds = end,
            Typed = text!.Trim(),
            Reason = reason,
        };

        return true;
    }

    /// <summary>The stretch as the track page shows it: bars with a warp, else m:ss, and always the time beside.</summary>
    public static string Describe(MeasuringStretch stretch, WarpGroup group)
    {
        var clock =
            $"{TempoFormat.Clock(stretch.StartSeconds)}–{TempoFormat.Clock(stretch.EndSeconds)}";

        return
            group.Lender is not null
            && group.BeatAt(stretch.StartSeconds) is { } start
            && group.BeatAt(stretch.EndSeconds) is { } end
            ? $"bars {BarText(group.Bars, start, end)} ({clock})"
            : clock;
    }

    private static string BarText(BarRuler bars, double start, double end)
    {
        var first = bars.Position(start);

        // Ending on a bar line reads as the bar before it, as it was typed.
        return IsBarLine(bars, end)
            ? $"{first}–{BarRuler.Number(bars.IndexAt(end - Snap))}"
            : $"{first}–{bars.Position(end)}";
    }

    private static bool IsBarLine(BarRuler bars, double beat)
    {
        var inBars = beat / bars.BeatsPerBar;

        return Math.Abs(inBars - Math.Round(inBars)) < Snap;
    }

    private static (double Start, double End)? ReadTimes(string? text) =>
        text is not null && JudgeRanges.TryParseTimes(text, out var start, out var end)
            ? (start, end)
            : null;

    private static (double Start, double End)? ReadBars(string? text, WarpGroup group)
    {
        if (text is null || RangePattern.Match(text) is not { Success: true } match)
            return null;

        var from = match.Groups["From"];
        var to = match.Groups["To"];
        var isOpenEnd = to.Value.Equals("end", StringComparison.OrdinalIgnoreCase);
        var isOpenStart = from.Value.Equals("start", StringComparison.OrdinalIgnoreCase);

        if (
            (isOpenStart ? 0 : SecondsAt(group, match, "From", isEnd: false)) is not { } start
            || (isOpenEnd ? EndOfFile(group) : SecondsAt(group, match, "To", isEnd: true))
                is not { } end
        )
            return null;

        return (Math.Max(start, 0), group.Seconds > 0 ? Math.Min(end, group.Seconds) : end);
    }

    private static double? EndOfFile(WarpGroup group) => group.Seconds > 0 ? group.Seconds : null;

    private static double? SecondsAt(WarpGroup group, Match match, string side, bool isEnd)
    {
        var bars = group.Bars;
        var unit = 4.0 / bars.Denominator;

        if (
            !int.TryParse(
                match.Groups[$"{side}Bar"].Value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var bar
            ) || bar is 0
        )
            return null;

        var beat = Count(match.Groups[$"{side}Beat"]);
        var sixteenth = Count(match.Groups[$"{side}Sixteenth"]);

        if (beat is < 1 || beat > bars.Numerator || sixteenth is < 1 || sixteenth > unit / 0.25)
            return null;

        var at =
            beat is null && sixteenth is null && isEnd
                ? bars.EndOf(bar)
                : bars.StartOf(bar) + ((beat ?? 1) - 1) * unit + ((sixteenth ?? 1) - 1) * 0.25;

        return group.SecondsAt(at);
    }

    private static int? Count(Group group) =>
        group.Success
            ? int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;

    [GeneratedRegex(
        @"
        ^\s*
        (?<From>start|(?<FromBar>-?\d{1,4})(\.(?<FromBeat>\d{1,2}))?(\.(?<FromSixteenth>\d{1,2}))?)
        (\s*[–—-]\s*|\s+)
        (?<To>end|(?<ToBar>-?\d{1,4})(\.(?<ToBeat>\d{1,2}))?(\.(?<ToSixteenth>\d{1,2}))?)
        \s*$
        ",
        RegexOptions.ExplicitCapture
            | RegexOptions.IgnorePatternWhitespace
            | RegexOptions.IgnoreCase
    )]
    private static partial Regex RangePattern { get; }
}
