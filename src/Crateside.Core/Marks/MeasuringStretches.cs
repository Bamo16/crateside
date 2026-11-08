using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;

namespace Crateside.Core.Marks;

/// <summary>Reads a stretch typed as Live's Time Selection reads it (<c>183–190</c>, <c>155.1.1–156.3.1</c>) or, without a warp, in time (<c>2:22–3:39</c>) (ADR 0085).</summary>
public static class MeasuringStretches
{
    /// <summary>
    /// Bars run from the warp's beat 0 as Live numbers them (ADR 0072), and the end is where the
    /// selection stops: <c>183–190</c> is bars 183 to 189. <c>start</c> and <c>end</c> are the file's edges.
    /// </summary>
    public static bool TryRead(
        string? text,
        string reason,
        WarpGroup group,
        [NotNullWhen(true)] out MeasuringStretch? stretch
    )
    {
        stretch = null;

        double startSeconds,
            endSeconds;

        if (group.Lender is null)
        {
            if (!JudgeRanges.TryParseTimes(text, out startSeconds, out endSeconds))
                return false;
        }
        else if (BarRanges.Read(text, group) is BarRangeReading.Read(var start, var end))
            (startSeconds, endSeconds) = (start, end);
        else
            return false;

        stretch = new MeasuringStretch
        {
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
            Typed = text!.Trim(),
            Reason = reason,
        };

        return true;
    }

    /// <summary>Why a stretch did not read, in the words the page shows.</summary>
    public static string Refusal(string? text, WarpGroup group) =>
        (group.Lender is not null, BarRanges.Read(text, group)) switch
        {
            (true, BarRangeReading.TooShort) => BarRanges.EmptyMessage,
            (true, _) => "Read as bars: 183–190, or 155.1.1–156.3.1 as Live's status bar shows it.",
            _ => "Read as time: 2:22–3:39.",
        };

    /// <summary>The stretch as the track page shows it: Live's positions with a warp, else m:ss, and always the time beside.</summary>
    public static string Describe(MeasuringStretch stretch, WarpGroup group)
    {
        var clock =
            $"{TempoFormat.Clock(stretch.StartSeconds)}–{TempoFormat.Clock(stretch.EndSeconds)}";

        return BarRanges.Show(group, stretch.StartSeconds, stretch.EndSeconds) is { } bars
            ? $"bars {bars} ({clock})"
            : clock;
    }
}
