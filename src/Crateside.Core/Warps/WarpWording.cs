using System.Globalization;
using Crateside.Core.Samples;
using Crateside.Core.Tempo;
using Humanizer;

namespace Crateside.Core.Warps;

/// <summary>How the warp view words a stem's warp: its chip, its lines, and whether each control applies (handoff 14).</summary>
/// <param name="nameOf">A stem's short name, as the sample list calls it.</param>
public sealed class WarpWording(WarpGroup group, Func<string, string> nameOf)
{
    public static string Time(double seconds)
    {
        var whole = (int)Math.Floor(seconds / 60);

        return $"{whole}:{(seconds - whole * 60).ToString("00.00", CultureInfo.InvariantCulture)}";
    }

    public static string Day(DateTime when) => when.ToString("d MMM", CultureInfo.CurrentCulture);

    /// <summary>A tempo between markers, which is exact as a warp's figure is.</summary>
    public static string Tempo(double bpm) => TempoFormat.Format(TrackTempo.Typed(bpm)).Text;

    public static string Mode(AbleKit.Analysis.WarpMode mode) =>
        mode switch
        {
            AbleKit.Analysis.WarpMode.RePitch => "Re-Pitch",
            AbleKit.Analysis.WarpMode.ComplexPro => "Complex Pro",
            AbleKit.Analysis.WarpMode.Rex => "REX",
            _ => mode.ToString(),
        };

    public WarpChip Chip(StemWarp stem) =>
        stem.Owner switch
        {
            WarpOwner.Yours => new("Your warp", ChipStyle.Filled),
            WarpOwner.Borrowed(var from, _) => new(
                $"Borrowed from {Name(from)}",
                ChipStyle.Outlined
            ),
            WarpOwner.Starter => new("Starter", ChipStyle.Dashed),
            WarpOwner.Unwarped => new("Warp off", ChipStyle.Dashed),
            WarpOwner.LiveAnalysis => new("Live's analysis", ChipStyle.Dashed),
            WarpOwner.WaitingForLive => new("Waiting for Live", ChipStyle.Amber),
            WarpOwner.PuttingBack => new("Putting the warp back", ChipStyle.Teal),
            _ => new("No warp yet", ChipStyle.Faint),
        };

    public string Sub(StemWarp stem) =>
        stem.Owner switch
        {
            WarpOwner.Yours(var saved, _, not null) =>
                $"saved over crateside's · {Day(saved)}{Differs(stem)}",
            WarpOwner.Yours(var saved, { Count: > 0 } lentTo, _) =>
                $"saved {Day(saved)} · lent to {Names(lentTo)}{Differs(stem)}",
            WarpOwner.Yours(var saved, _, _) => $"saved {Day(saved)}{Differs(stem)}",
            WarpOwner.Borrowed(_, var written) => $"unchanged since {Day(written)}",
            WarpOwner.Starter => "one marker · not finished",
            WarpOwner.Unwarped => "gain and colour only · warp it by hand",
            WarpOwner.LiveAnalysis => "not saved, so Live ignores it",
            WarpOwner.WaitingForLive => "preview it in Live",
            WarpOwner.PuttingBack => "Live opened it",
            _ => "Live has not opened it",
        };

    public string Sentence(StemWarp stem) =>
        stem.Owner switch
        {
            WarpOwner.Yours(var saved, _, { } over) =>
                $"crateside {WhatItWrote(over)} on {Day(over.Written.ToLocalTime())}; you saved over it in Live on {Day(saved)}, so it is yours now.{FollowsUntil(stem)}",
            WarpOwner.Yours(var saved, { Count: > 0 } lentTo, _) =>
                $"Saved in Live on {Day(saved)}. crateside lends it to {Names(lentTo)}.",
            WarpOwner.Yours(var saved, _, _) =>
                $"Saved in Live on {Day(saved)}.{FollowsUntil(stem)}",
            WarpOwner.Borrowed(var from, var written) =>
                $"crateside copied {Name(from)}'s warp on {Day(written)}. Unchanged since.",
            WarpOwner.Starter =>
                $"One marker at crateside's reading{StarterTempo(stem)}, so dropping it into an empty set sets the set's tempo. Not a finished warp: warp it in Live and press Save Default Clip.",
            WarpOwner.Unwarped(var written) =>
                $"crateside saved its clip with Warp off on {Day(written)}, so it comes in at the loudness target in its colour. Its tempo drifts or is not read yet, so it has no starter: warp it in Live and press Save Default Clip.",
            WarpOwner.LiveAnalysis =>
                "Live has opened it, but no warp is saved. Live ignores these markers until you press Save Default Clip.",
            WarpOwner.WaitingForLive =>
                "Its warp file is in the Recycle Bin. Preview it in Live's browser and Live writes a fresh one; crateside then puts the warp back.",
            WarpOwner.PuttingBack =>
                "Live wrote a fresh file. crateside puts the warp back within a minute.",
            _ => "Live has not opened this file, so there is no warp and no overview yet.",
        };

    /// <summary>Whether re-applying would change anything: a sibling holds a warp of the user's, and this stem's differs from it.</summary>
    public bool CanReapply(StemWarp stem) =>
        group.Lender is { Owner: WarpOwner.Yours } lender
        && lender != stem
        && stem.Owner switch
        {
            WarpOwner.Yours => group.PartsFrom(stem) is not null,
            WarpOwner.LiveAnalysis or WarpOwner.Starter or WarpOwner.Unwarped => true,
            _ => false,
        };

    public string ReapplyStatus(StemWarp stem) =>
        (CanReapply(stem), stem.Owner, group.Lender) switch
        {
            (true, WarpOwner.Yours, { } lender) =>
                $"Writes {Name(lender.Path)}'s warp over yours again. Yours is backed up first.",
            (true, WarpOwner.Starter, { } lender) =>
                $"Writes {Name(lender.Path)}'s warp over the starter. The starter is backed up first.",
            (true, WarpOwner.Unwarped, { } lender) =>
                $"Writes {Name(lender.Path)}'s warp over the clip with Warp off. It is backed up first.",
            (true, _, { } lender) =>
                $"Writes {Name(lender.Path)}'s warp over Live's analysis again. Live's is backed up first.",
            (_, WarpOwner.Yours(_, { Count: > 0 }, _), _) =>
                "Nothing to re-apply: this is the warp crateside lends.",
            (_, WarpOwner.Yours, _) => "Nothing to re-apply: crateside has lent this stem nothing.",
            (_, WarpOwner.Borrowed(var from, _), _) =>
                $"Nothing to re-apply: it is still {Name(from)}'s warp, unchanged.",
            (_, WarpOwner.Starter or WarpOwner.LiveAnalysis or WarpOwner.Unwarped, _) =>
                "Nothing to lend yet: no stem of this upload has a saved warp.",
            _ => "Nothing to re-apply.",
        };

    public string ReapplyQuestion(StemWarp stem) =>
        (stem.Owner, group.Lender) switch
        {
            (WarpOwner.Yours(var saved, _, _), { } lender) =>
                $"Replace your warp on {Name(stem.Path)} with {Name(lender.Path)}'s? Yours ({"marker".ToQuantity(stem.Markers.Count)}, saved {Day(saved)}) is backed up first.",
            (_, { } lender) =>
                $"Replace what {Name(stem.Path)} holds with {Name(lender.Path)}'s warp? It is backed up first.",
            _ => string.Empty,
        };

    public string ReapplyConfirm(StemWarp stem) =>
        stem.Owner is WarpOwner.Yours ? "Replace my warp" : "Replace";

    /// <summary>Whether the track has a warp of the user's, on a stem it is warped by, measured off its tempo (#96).</summary>
    public bool CanTrueUp(TrackTempo? tempo) =>
        group.Lender is { IsAuthority: true, Owner: WarpOwner.Yours }
        && tempo is { Off.DriftMs: not null };

    public string TrueUpStatus(TrackTempo tempo, TrueUpPlan.Retype plan) =>
        tempo.Off is { } off
            ? $"Runs {TempoFormat.Offset(tempo.Measured, off.Bpm)}, so its grid falls {Math.Abs(plan.Verdict.DriftMs):0} ms {(plan.Verdict.DriftMs < 0 ? "behind" : "ahead")} by the end. Retypes it to {Thousandths(plan.Verdict.Bpm)} and lines it up with {Transients(plan)}. Yours is backed up first."
            : string.Empty;

    public string TrueUpQuestion(TrueUpPlan.Retype plan) =>
        $"Retype your warp from {Figure(plan.Verdict.WarpBpm)} to {Thousandths(plan.Verdict.Bpm)} and line its first beat up with {Transients(plan)}? The grid moves by up to {Math.Abs(plan.Verdict.DriftMs):0} ms, most at the end."
        + (
            plan.Lent.Count > 0
                ? $" {Names(plan.Lent)} {(plan.Lent.Count is 1 ? "borrows this warp and moves" : "borrow this warp and move")} with it."
                : string.Empty
        )
        + (
            group.Lender is { Owner: WarpOwner.Yours(var saved, _, _), Markers.Count: var markers }
                ? $" Yours ({"marker".ToQuantity(markers)}, saved {Day(saved)}) is backed up first."
                : " Yours is backed up first."
        );

    public static string TrueUpWorking(TrueUpPlan.Retype plan) =>
        $"Retyping the warp to {Thousandths(plan.Verdict.Bpm)}… Yours is backed up.";

    private string Transients(TrueUpPlan.Retype plan) =>
        plan.PhasedFrom is null ? $"{Name(group.Lender!.Path)}'s transients" : "the drums";

    private static string Figure(double bpm) => bpm.ToString("0.###", CultureInfo.InvariantCulture);

    public static string TrueUpRefusal(TrueUpPlan plan) =>
        plan switch
        {
            TrueUpPlan.Left(TrueUpReason.DoesNotHold) =>
                $"Not offered: the grid would not hold within {WarpTrueUp.HoldsWithinMs:0} ms all through the track.",
            TrueUpPlan.Left(TrueUpReason.WithinNoise) =>
                "Not offered: the warp already holds against the audio.",
            TrueUpPlan.Left(TrueUpReason.Steps) =>
                "Not offered: the grid steps partway through, which a new tempo does not fix.",
            TrueUpPlan.Left => "Not offered: crateside could not check the warp against the audio.",
            TrueUpPlan.Disagree =>
                "Not offered: the stems the track is warped by hold different warps.",
            TrueUpPlan.Failed(var error) => $"Not offered: {error}",
            _ => "Not offered.",
        };

    public static string TrueUpDone(TrueUpOutcome outcome, string ago) =>
        outcome switch
        {
            TrueUpOutcome.Retyped(var bpm, _) =>
                $"Trued up {ago}: {Thousandths(bpm)}, holding within {WarpTrueUp.HoldsWithinMs:0} ms all through the track. Your previous warp is backed up.",
            TrueUpOutcome.Changed =>
                "Not retyped: the warp was saved again since crateside looked at it.",
            TrueUpOutcome.Failed(var error) => $"Not retyped: {error}",
            _ => "Not retyped.",
        };

    private static string Thousandths(double bpm) =>
        bpm.ToString("0.000", CultureInfo.InvariantCulture);

    public static bool CanLetLiveAnalyse(StemWarp stem) =>
        stem.Sidecar is not null
        && stem.Owner is not (WarpOwner.WaitingForLive or WarpOwner.PuttingBack);

    public string LetLiveStatus(StemWarp stem, string folder) =>
        stem.Owner switch
        {
            WarpOwner.WaitingForLive =>
                $"Preview {Name(stem.Path)} in Live's browser ({folder}). Live writes a fresh file, and crateside puts the warp back within a minute.",
            WarpOwner.PuttingBack => "Live wrote a fresh file · putting the warp back…",
            _ when stem.Sidecar is null => "No warp file yet: Live has not opened this stem.",
            _ =>
                "Sends this stem's warp file to the Recycle Bin, so Live analyses it afresh with its own transients. crateside then puts the warp back.",
        };

    public string LetLiveQuestion(StemWarp stem) =>
        $"Send {Name(stem.Path)}'s warp file to the Recycle Bin? Until Live opens the stem again, it has no warp.";

    public string MarkersNote(StemWarp stem) =>
        (stem.Owner, group.PartsFrom(stem), group.Lender) switch
        {
            (WarpOwner.Borrowed(var from, _), _, _) => $"same as {Name(from)}",
            (_, not null, _) => "its own",
            (_, null, { } lender) when lender == stem || stem.IsWarped => "shared by the group",
            _ => string.Empty,
        };

    public static string TransientsNote(StemWarp stem) =>
        stem.TransientOrigin switch
        {
            null => "Live's",
            { Detector: not null } => "crateside's",
            _ => "from the earlier cut",
        };

    public string Name(string path) =>
        group.Stems.FirstOrDefault(stem =>
            string.Equals(stem.Path, path, StringComparison.OrdinalIgnoreCase)
        )
            is { } stem
            ? nameOf(stem.Path)
            : StemFileName.PresetOf(path) ?? "the source";

    private string Names(IEnumerable<string> paths) => paths.Select(Name).Humanize();

    private string Differs(StemWarp stem) =>
        group.PartsFrom(stem) is { } parts
            ? $" · differs from {TempoFormat.Clock(parts)}"
            : string.Empty;

    private string FollowsUntil(StemWarp stem) =>
        (group.PartsFrom(stem), group.Lender) switch
        {
            ({ } parts, { } lender) =>
                $" It follows {Name(lender.Path)} up to {TempoFormat.Clock(parts)}, then has markers of its own.",
            _ => string.Empty,
        };

    private string WhatItWrote(WrittenWarp warp) =>
        warp switch
        {
            { From: { } from } => $"lent it {Name(from)}'s warp",
            { IsUnwarped: true } => "saved its clip with Warp off",
            _ => "gave it a starter warp",
        };

    private static string StarterTempo(StemWarp stem) =>
        stem.Sidecar?.Warp.TempoAt(0) is { } bpm ? $", {Tempo(bpm)}" : string.Empty;
}

public sealed record WarpChip(string Label, ChipStyle Style);

public enum ChipStyle
{
    Filled,
    Outlined,
    Dashed,
    Faint,
    Amber,
    Teal,
}
