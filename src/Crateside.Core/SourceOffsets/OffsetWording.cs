using System.Globalization;
using Crateside.Core.Warps;

namespace Crateside.Core.SourceOffsets;

/// <summary>What an added source's measurement and save say on screen (ADR 0111, rules 21 and 22).</summary>
public static class OffsetWording
{
    private const char NoBreakSpace = (char)0xA0;

    /// <summary>The one line on Track details, by verdict.</summary>
    public static string Line(OffsetReading reading) =>
        reading switch
        {
            { Verdict: OffsetVerdict.Aligned, Offset: { } samples } =>
                $"Aligned to the mix: {WarpWording.Offset(reading.OffsetMilliseconds)} ({Grouped(samples)} samples), {Windows(reading)}",
            { Verdict: OffsetVerdict.Edit } =>
                $"Cut differently from the mix: {RunsText(reading)}; not written. Split the clip in Live where the cut is",
            { Verdict: OffsetVerdict.Drift, SpeedFactor: not null, DriftPpm: { } ppm } =>
                $"Runs {Math.Abs(ppm) / 1e4:0.#}% {(ppm > 0 ? "fast" : "slow")} against the mix; use the audio release; not written",
            { Verdict: OffsetVerdict.Drift, DriftPpm: { } ppm } =>
                $"Runs at another speed than the mix: {ppm.ToString("+0;-0;0", CultureInfo.InvariantCulture)} ppm; not written",
            _ => $"Does not line up with the mix: {Windows(reading)}; not written",
        };

    /// <summary>The spans an aligned source has that the other file lacks and that this window covers, one phrase each.</summary>
    public static IReadOnlyList<string> WindowNotes(OffsetReading reading, OffsetWindow window)
    {
        var end = window.Start + reading.WindowSeconds;
        var shift = reading.OffsetMilliseconds / 1000;

        return
        [
            .. reading
                .OnlyInSource.Where(span => span.From < end && span.To > window.Start)
                .Select(span => $"only in this file: {Span(span)}"),
            .. reading
                .OnlyInMix.Where(span => span.From < end + shift && span.To > window.Start + shift)
                .Select(span => $"only in the mix: {Span(span)}"),
        ];
    }

    /// <summary>What the added source's lane in the warp view says beside its label.</summary>
    public static string Lane(OffsetReading reading) =>
        $"shifted {WarpWording.Offset(reading.OffsetMilliseconds)}";

    /// <summary>The line the warp view's save lists for an added source.</summary>
    public static string Saves(AddedSourcePlan plan) =>
        plan switch
        {
            { IsWritten: true, OffsetMilliseconds: { } ms } =>
                $"{plan.File.Label}: shifted by {WarpWording.Offset(ms)}",
            { Skip: { } skip } => $"{plan.File.Label}: not written, {Why(skip)}",
            _ => plan.File.Label,
        };

    public static string Why(AddedSkip skip) =>
        skip switch
        {
            AddedSkip.Edit => "cut differently from the mix",
            AddedSkip.Drift => "runs at another speed than the mix",
            AddedSkip.NotTheSameRecording => "does not line up with the mix",
            AddedSkip.HasOwnWarp => "has its own warp",
            AddedSkip.NoSidecar => "no analysis file yet",
            _ => "still being measured",
        };

    public static string Span(OffsetSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{span.From:0.#}-{span.To:0.#} s");

    /// <summary>Seconds as a signed value to the hundredth, as an edit's runs are said.</summary>
    private static string Seconds(double samples, int rate) =>
        (samples / rate).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " s";

    private static string RunsText(OffsetReading reading) =>
        string.Join(
            ", ",
            reading.Runs.Select(
                (run, index) =>
                    index is 0 && run.From is 0
                        ? FormattableString.Invariant(
                            $"{Seconds(run.Offset, reading.SampleRate)} up to {run.To:0.#} s"
                        )
                        : FormattableString.Invariant(
                            $"{Seconds(run.Offset, reading.SampleRate)} from {run.From:0.#} s to {run.To:0.#} s"
                        )
            )
        );

    private static string Windows(OffsetReading reading) =>
        $"{reading.ConfidentWindows} of {reading.AwakeWindows} windows";

    private static string Grouped(int samples) =>
        samples.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
