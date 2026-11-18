using AbleKit.Analysis;

namespace Crateside.Core.Warps;

/// <summary>A scaling of the grid's tempo about a beat that keeps its place in the audio (ADR 0107).</summary>
/// <param name="Factor">Beats per second are multiplied by this: 0.5 halves the tempo.</param>
public readonly record struct TempoStep(double Factor, double AnchorBeat);

/// <summary>
/// The edits the warp view makes to a warp before saving it: the draft. Each kind is kept apart, so
/// the order they were made in does not change the result (ADR 0107).
/// </summary>
public sealed record WarpDraft
{
    /// <summary>How many whole beats later the grid sits under the audio; negative for earlier.</summary>
    public int BarShift { get; init; }

    public double NudgeMs { get; init; }

    public IReadOnlyList<TempoStep> Tempo { get; init; } = [];

    /// <summary>The clip's start as a whole bar number; null leaves it where it is.</summary>
    public int? ClipStartBar { get; init; }

    public double TempoFactor => Tempo.Aggregate(1.0, (factor, step) => factor * step.Factor);

    public WarpDraft Later(int beats) => this with { BarShift = BarShift + beats };

    public WarpDraft Nudged(double ms) => this with { NudgeMs = NudgeMs + ms };

    /// <summary>A step in the other direction about the same beat takes the last one back.</summary>
    public WarpDraft Scaled(double factor, double anchorBeat) =>
        Tempo is [.., { Factor: var last, AnchorBeat: var at }]
        && at == anchorBeat
        && Math.Abs(last * factor - 1) < 1e-9
            ? this with
            {
                Tempo = [.. Tempo.Take(Tempo.Count - 1)],
            }
            : this with
            {
                Tempo = [.. Tempo, new TempoStep(factor, anchorBeat)],
            };

    public WarpDraft WithClipStart(int bar) => this with { ClipStartBar = bar };
}

/// <summary>Applies a <see cref="WarpDraft"/> to a warp's markers and default clip. Pure over AbleKit's types.</summary>
public static class WarpEdit
{
    /// <summary>The clip's start in beats once the draft is applied: a whole bar if the draft set one, else where it was.</summary>
    public static double ClipStartBeat(AnalysisFile file, WarpDraft draft) =>
        draft.ClipStartBar is { } bar
            ? BarRuler.Of(file.Warp.TimeSignature).StartOf(bar)
            : file.DefaultClip?.Start ?? 0;

    /// <summary>The file with the draft's edits made and its default clip saved; the file itself when it has no warp to edit.</summary>
    public static AnalysisFile Apply(AnalysisFile file, WarpDraft draft)
    {
        if (file.Warp.Markers is not { Count: > 1 } markers)
            return file;

        var edited = file.Warp with
        {
            Markers =
            [
                .. markers.Select(marker => new WarpMarker(
                    marker.Seconds + draft.NudgeMs / 1000,
                    Beat(marker.Beat, draft)
                )),
            ],
        };
        var start = ClipStartBeat(file, draft);
        var clip = file.Clip;
        var loopStart = Moved(file.Warp, edited, draft, clip.LoopStart);

        return file with
        {
            Warp = edited,
            IsDefaultClipSaved = true,
            Clip = clip with
            {
                LoopStart = clip.LoopOn ? loopStart : start,
                LoopEnd = Moved(file.Warp, edited, draft, clip.LoopEnd),
                HiddenLoopStart = Moved(file.Warp, edited, draft, clip.HiddenLoopStart),
                HiddenLoopEnd = Moved(file.Warp, edited, draft, clip.HiddenLoopEnd),
                OutMarker = Moved(file.Warp, edited, draft, clip.OutMarker),
                SampleOffset = clip.LoopOn ? start - loopStart : clip.SampleOffset,
            },
        };
    }

    /// <summary>A beat at a time in the audio, extending the first and last stretches past the markers.</summary>
    public static double? BeatAt(Warp warp, double seconds)
    {
        if (warp.Markers is not { Count: > 1 } markers)
            return null;

        var i = 1;

        while (i < markers.Count - 1 && seconds > markers[i].Seconds)
            i++;

        var (from, to) = (markers[i - 1], markers[i]);

        return from.Beat
            + (seconds - from.Seconds) * (to.Beat - from.Beat) / (to.Seconds - from.Seconds);
    }

    private static double Beat(double beat, WarpDraft draft) =>
        draft.Tempo.Aggregate(
            beat,
            (current, step) => step.AnchorBeat + (current - step.AnchorBeat) * step.Factor
        ) - draft.BarShift;

    /// <summary>Where a beat of the old grid lands on the edited one, keeping its place in the audio.</summary>
    private static double Moved(Warp from, Warp to, WarpDraft draft, double beat) =>
        from.SecondsAt(beat) is { } seconds
            ? BeatAt(to, seconds + draft.NudgeMs / 1000) ?? beat
            : beat;
}
