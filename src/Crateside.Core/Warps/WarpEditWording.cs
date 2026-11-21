using System.Globalization;
using AbleKit.Analysis;
using Humanizer;

namespace Crateside.Core.Warps;

/// <summary>What one cell of the edit bar says, and whether it differs from the warp on disk.</summary>
public sealed record EditReadout(string Text, bool Changed);

/// <summary>The words of the warp view's edit bar: each cell's readout against the warp on disk (handoff 24).</summary>
public static class WarpEditWording
{
    private const double Tolerance = 1e-6;

    public static EditReadout Bar(
        AnalysisFile disk,
        AnalysisFile drafted,
        WarpDraft draft,
        double seconds,
        bool starter
    )
    {
        var changed = draft.BarShift is not 0 || Math.Abs(draft.TempoFactor - 1) > 1e-9;
        var (where, was) = (Where(drafted, seconds), Where(disk, seconds));

        if (where.Outside || was.Outside)
            return new(
                $"the marker falls on {where.Falls} · {(starter ? "crateside put it" : "it was")} on {was.Falls}",
                changed
            );

        return changed
            ? new(
                $"bar 1 starts on the marker's beat {where.Beat}"
                    + (
                        starter
                            ? $" · crateside put it on beat {was.Beat}"
                            : $" · was beat {was.Beat}"
                    ),
                true
            )
            : new(
                $"bar 1 starts on the marker's beat {where.Beat}, {(starter ? "where crateside put it" : "as saved")}",
                false
            );
    }

    public static EditReadout Nudge(WarpDraft draft, bool starter) =>
        draft.NudgeMs is 0
            ? new(starter ? "on crateside's marker" : "as saved", false)
            : new(
                $"{Signed(draft.NudgeMs)} ms from {(starter ? "where crateside put it" : "the saved warp")}",
                true
            );

    public static EditReadout Tempo(AnalysisFile disk, AnalysisFile drafted, WarpDraft draft)
    {
        var (was, now) = (TempoOf(disk), TempoOf(drafted));

        return Math.Abs(draft.TempoFactor - 1) > 1e-9
            ? new($"{WarpWording.Tempo(was)} → {WarpWording.Tempo(now)}", true)
            : new(WarpWording.Tempo(was), false);
    }

    /// <summary>The tempo gutter's figure while the tempo is changed: <c>64.00 was 128.00</c>; null when it is not.</summary>
    public static string? DraftTempo(AnalysisFile disk, AnalysisFile drafted, WarpDraft draft) =>
        Math.Abs(draft.TempoFactor - 1) > 1e-9
            ? $"{WarpWording.Tempo(TempoOf(drafted))} was {WarpWording.Tempo(TempoOf(disk))}"
            : null;

    public static EditReadout ClipStart(AnalysisFile disk, WarpDraft draft)
    {
        var ruler = BarRuler.Of(disk.Warp.TimeSignature);
        var was = Position(ruler, disk.DefaultClip?.Start ?? 0);

        return draft.ClipStartBar is { } bar && Position(ruler, ruler.StartOf(bar)) != was
            ? new($"clip start · bar {bar} · was bar {was}", true)
            : new($"clip start · bar {was}", false);
    }

    /// <summary>The lines of the foot's <c>Saves to</c>: where it writes, which stems keep their own warp, which it lends on to.</summary>
    public static IReadOnlyList<string> SavesTo(
        IReadOnlyList<string> saving,
        IReadOnlyList<string> keepsOwn,
        IReadOnlyList<string> lent
    ) =>
        [
            $"Saves to {saving.Humanize()}",
            .. keepsOwn is []
                ? (string[])[]
                :
                [
                    $"{keepsOwn.Humanize()} {(keepsOwn.Count is 1 ? "keeps its own warp" : "keep their own warps")}",
                ],
            .. lent is [] ? (string[])[] : [$"then lends it again to {lent.Humanize()}"],
        ];

    /// <summary>The edits a draft holds, as the leave prompt and a saved lane say them.</summary>
    public static string Changes(WarpDraft draft) =>
        WarpWording.Changes(
            new SavedEdits(
                draft.BarShift,
                draft.NudgeMs,
                draft.TempoFactor,
                draft.ClipStartBar,
                null,
                false
            )
        );

    private static (bool Outside, string Beat, string Falls) Where(
        AnalysisFile file,
        double seconds
    )
    {
        var ruler = BarRuler.Of(file.Warp.TimeSignature);
        var markerBeat = file.Warp.Markers[0].Beat;
        var unit = 4.0 / ruler.Denominator;
        var within = (-markerBeat % ruler.BeatsPerBar + ruler.BeatsPerBar) % ruler.BeatsPerBar;

        if (ruler.BeatsPerBar - within < Tolerance)
            within = 0;

        var bar = ruler.BarAt(markerBeat + Tolerance);
        var falls =
            $"bar {bar}, beat {Beats(1 + (markerBeat + Tolerance - ruler.StartOf(bar)) / unit)}";
        var bar1 = file.Warp.SecondsAt(0);

        return (
            bar1 is null or < 0
                || bar1 > seconds
                || Math.Abs(markerBeat) > ruler.BeatsPerBar + Tolerance,
            Beats(1 + within / unit),
            falls
        );
    }

    /// <summary>A beat number: whole, or with a half as <c>2½</c>.</summary>
    private static string Beats(double beat)
    {
        var whole = (int)Math.Floor(beat + Tolerance);
        var fraction = beat - whole;

        return fraction switch
        {
            < Tolerance => $"{whole}",
            _ when Math.Abs(fraction - 0.5) < 1e-3 => $"{whole}½",
            _ => beat.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    private static string Position(BarRuler ruler, double beat) =>
        ruler.IsBarLine(beat) ? $"{ruler.BarAt(beat + Tolerance)}" : ruler.Position(beat);

    private static double TempoOf(AnalysisFile file) =>
        file.Warp.TempoAt(file.Warp.SecondsAt(file.DefaultClip?.Start ?? 0) ?? 0) ?? 0;

    private static string Signed(double ms) =>
        ms.ToString("+0.##;−0.##", CultureInfo.InvariantCulture);
}
