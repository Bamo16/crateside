using AbleKit.Analysis;
using Crateside.Core.Samples;
using Crateside.Core.SourceOffsets;

namespace Crateside.Core.Warps;

/// <summary>
/// Saves a <see cref="WarpDraft"/> over a track's warp: every stem that holds it, backed up, then the
/// copies crateside lent from them written again (ADR 0107). Undo puts the backups back.
/// </summary>
public sealed class WarpEditor(
    WrittenWarps written,
    SavedWarps saved,
    SidecarBackups backups,
    WarpActions actions,
    TimeProvider time,
    SourceOffsetStore? offsets = null
)
{
    /// <summary>Which stems a save would write, without writing; taken when the draft begins, so the save can tell the files changed since.</summary>
    public WarpSavePlan Plan(
        WarpSiblings siblings,
        SiblingFile song,
        IReadOnlyList<AddedSourceFile>? added = null
    )
    {
        try
        {
            var cuts = siblings.CutsOf(song).ToArray();
            var holders = cuts.Where(cut => written.BorrowedFrom(cut.Path) is null)
                .Select(cut => (Cut: cut, Analysis: Warped(cut.Path)))
                .Where(stem => stem.Analysis is not null)
                .ToArray();
            var authorities = holders.Where(stem => stem.Cut.IsAuthority).ToArray();

            // A track kept only as added sources has no authority: the warp guide, placed in Live, stands in (ADR 0111, rule 24).
            if (authorities is [] && added is { Count: > 0 })
                authorities = holders;

            if (authorities is [])
                return new WarpSavePlan.NoWarp();

            if (
                authorities.Skip(1).Any(stem => !SameWarp(stem.Analysis!, authorities[0].Analysis!))
            )
                return new WarpSavePlan.Disagree([.. authorities.Select(stem => stem.Cut.Path)]);

            var reference = WarpSiblings
                .TempoSources(authorities.Select(stem => stem.Cut))
                .Concat(authorities.Select(stem => stem.Cut))
                .First();
            var markers = authorities.First(stem => stem.Cut == reference).Analysis!.Warp.Markers;
            var sharing = holders
                .Where(stem =>
                    stem.Cut.IsAuthority || stem.Analysis!.Warp.Markers.SequenceEqual(markers)
                )
                .Select(stem => stem.Cut.Path)
                .ToArray();

            return new WarpSavePlan.Ready(
                reference.Path,
                [.. sharing.Select(SidecarStamp.Of)],
                // A copy is named by what its sidecar says, not by the lender's path, which moves.
                [
                    .. sharing
                        .SelectMany(written.LentTo)
                        .Concat(
                            cuts.Where(cut => written.BorrowedFrom(cut.Path) is not null)
                                .Select(cut => cut.Path)
                        )
                        .Distinct(StringComparer.OrdinalIgnoreCase),
                ],
                [
                    .. holders
                        .Select(stem => stem.Cut.Path)
                        .Except(sharing, StringComparer.OrdinalIgnoreCase),
                ]
            )
            {
                Added =
                    offsets is null || added is null
                        ? []
                        : AddedSourcePlan.Of(added, offsets, written),
            };
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new WarpSavePlan.Failed(ex.Message);
        }
    }

    /// <summary>
    /// A save for a track with no stem to hold the warp: the first aligned added source's warp, put back on the mix's
    /// timeline, is what the draft edits, and every aligned added source is written from it (ADR 0111, rule 23).
    /// </summary>
    public WarpSavePlan PlanFromAdded(IReadOnlyList<AddedSourceFile> added)
    {
        if (offsets is null)
            return new WarpSavePlan.NoWarp();

        var plans = AddedSourcePlan.Of(added, offsets, written);

        if (MixLaneOf(added, offsets) is not { } lane)
            return new WarpSavePlan.NoWarp();

        return new WarpSavePlan.Ready(lane.Source.Path, [], [], [])
        {
            Added = plans,
            MixWarp = lane.Mix,
        };
    }

    /// <summary>The added source the warp is read from and its sidecar on the mix's timeline: an instrumental before an acappella, then plan order.</summary>
    public static MixLane? MixLaneOf(
        IReadOnlyList<AddedSourceFile> added,
        SourceOffsetStore offsets
    )
    {
        foreach (var file in added.OrderBy(file => file.Type is SampleType.Instrumental ? 0 : 1))
        {
            if (
                offsets.Of(file.SourceKey)
                    is not {
                        IsUsable: true,
                        Reading: { Verdict: OffsetVerdict.Aligned, Offset: not null } reading
                    }
                || !AnalysisFile.TryRead(StemFileName.AnalysisFor(file.Path), out var sidecar)
                || sidecar
                    is not {
                        IsDefaultClipSaved: true,
                        Warp: { IsWarped: true, Markers.Count: > 1 }
                    }
            )
                continue;

            return new MixLane(
                file,
                ShiftedWarp.OnMixTimeline(sidecar, reading.Offset.Value, reading.SampleRate),
                reading
            );
        }

        return null;
    }

    public async Task<WarpSaveOutcome> SaveAsync(
        WarpSavePlan.Ready plan,
        WarpSiblings siblings,
        WarpDraft draft,
        Action<int>? progress = null,
        CancellationToken ct = default
    )
    {
        List<SavedStem> done = [];
        List<SavedStem> shifted = [];

        if (plan.Added.FirstOrDefault(item => item.IsChanged) is { Stamp.Path: var changedAdded })
            return new WarpSaveOutcome.Changed(changedAdded);

        try
        {
            foreach (var stamp in plan.Stems)
            {
                if (SidecarStamp.Of(stamp.Path) != stamp)
                {
                    Restore(done);

                    return new WarpSaveOutcome.Changed(stamp.Path);
                }

                var sidecar = StemFileName.AnalysisFor(stamp.Path);
                var file = AnalysisFile.Read(sidecar);
                var was = written.Of(stamp.Path);
                var backup = backups.Keep(sidecar);
                var edits = Describe(file, draft, was);

                WarpEdit.Apply(file, draft).Write(sidecar);
                done.Add(new SavedStem(stamp.Path, backup, was, SidecarStamp.Of(stamp.Path)));
                saved.Record(stamp.Path, edits, time.GetUtcNow());
                progress?.Invoke(done.Count);
            }

            shifted.AddRange(ShiftAddedSources(plan, draft));
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
            )
        {
            Restore([.. done, .. shifted]);

            return new WarpSaveOutcome.Failed(ex.Message);
        }

        List<SavedStem> lent = [];

        foreach (var path in plan.Lent)
        {
            ct.ThrowIfCancellationRequested();

            if (WarpSiblings.Read(path) is not { } copy)
                continue;

            var sidecar = StemFileName.AnalysisFor(path);

            try
            {
                var backup = backups.Keep(sidecar);
                var was = written.Of(path);

                await actions.ReapplyAsync(
                    path,
                    siblings.AuthoritiesFor(copy),
                    siblings.SameKindAs(copy),
                    ct
                );
                lent.Add(new SavedStem(path, backup, was, SidecarStamp.Of(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The copy keeps what it held; re-applying it from the warp view mends it.
            }
        }

        return new WarpSaveOutcome.Saved(new WarpSaved(time.GetUtcNow(), done, lent, shifted));
    }

    /// <summary>Derives each added source's sidecar from the mix's warp as saved, backing each up first (ADR 0111, rules 14 to 20).</summary>
    private List<SavedStem> ShiftAddedSources(WarpSavePlan.Ready plan, WarpDraft draft)
    {
        List<SavedStem> shifted = [];
        var mix =
            plan.MixWarp is { } onMixTimeline ? WarpEdit.Apply(onMixTimeline, draft)
            : AnalysisFile.TryRead(StemFileName.AnalysisFor(plan.Reference), out var onDisk)
                ? onDisk
            : null;

        if (mix is null || !plan.Added.Any(item => item.IsWritten))
            return shifted;

        try
        {
            foreach (
                var item in plan.Added.Where(item =>
                    item
                        is {
                            IsWritten: true,
                            Record: { IsUsable: true, Reading: { Offset: not null } }
                        }
                )
            )
            {
                var path = item.File.Path;
                var sidecar = StemFileName.AnalysisFor(path);
                var reading = item.Record!.Reading;
                var source = AnalysisFile.Read(sidecar);

                if (
                    source.Audio.HeadTable is not [.., > 0 and var frames]
                    || ShiftedWarp.Derive(
                        mix,
                        source,
                        reading.Offset!.Value,
                        reading.SampleRate,
                        (double)frames / reading.SampleRate
                    )
                        is not { } derived
                )
                    continue;

                var backup = backups.Keep(sidecar);
                var was = written.Of(path);

                derived.File.Write(sidecar);
                written.RecordShifted(
                    path,
                    new WrittenShift(
                        reading.Offset.Value,
                        reading.OffsetMilliseconds,
                        derived.ClipStartWasBar,
                        derived.ClipStartNowBar
                    )
                );
                shifted.Add(new SavedStem(path, backup, was, SidecarStamp.Of(path)));
            }
        }
        catch
        {
            Restore(shifted);

            throw;
        }

        return shifted;
    }

    /// <summary>Puts every sidecar the save wrote back as it was, if none has been saved over since.</summary>
    public WarpUndoOutcome Undo(WarpSaved save)
    {
        foreach (var stem in save.Stems)
        {
            if (SidecarStamp.Of(stem.Path) != stem.After)
                return new WarpUndoOutcome.Changed(stem.Path);
        }

        try
        {
            Restore(save.Stems);

            foreach (
                var stem in save
                    .Lent.Concat(save.Shifted)
                    .Where(stem => SidecarStamp.Of(stem.Path) == stem.After)
            )
                Restore([stem]);

            return new WarpUndoOutcome.Undone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WarpUndoOutcome.Failed(ex.Message);
        }
    }

    private void Restore(IEnumerable<SavedStem> stems)
    {
        foreach (var (path, backup, was, _) in stems)
        {
            File.Copy(backup, StemFileName.AnalysisFor(path), overwrite: true);
            saved.Forget(path);

            if (was is not null)
                written.Rerecord(path, was);
        }
    }

    private static SavedEdits Describe(AnalysisFile file, WarpDraft draft, WrittenWarp? was) =>
        new(
            draft.BarShift,
            draft.NudgeMs,
            draft.TempoFactor,
            draft.ClipStartBar,
            file.DefaultClip is { Start: var start }
                ? BarRuler.Of(file.Warp.TimeSignature).BarAt(start)
                : null,
            was is { IsStarter: true }
        );

    private static AnalysisFile? Warped(string audioPath) =>
        AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
        && analysis is { IsDefaultClipSaved: true, Warp: { IsWarped: true, Markers.Count: > 1 } }
            ? analysis
            : null;

    private static bool SameWarp(AnalysisFile left, AnalysisFile right) =>
        left.DefaultClip == right.DefaultClip
        && left.Warp.Markers.SequenceEqual(right.Warp.Markers);
}

public abstract record WarpSavePlan
{
    private WarpSavePlan() { }

    /// <param name="Reference">The stem whose tempo and clip the draft is drawn against.</param>
    /// <param name="Stems">The stems written, as they stood when the draft began.</param>
    /// <param name="Lent">The stems crateside copied a warp to from <paramref name="Stems"/>, written again afterwards.</param>
    /// <param name="KeepsOwn">The stems holding a warp of their own, left as they are.</param>
    public sealed record Ready(
        string Reference,
        IReadOnlyList<SidecarStamp> Stems,
        IReadOnlyList<string> Lent,
        IReadOnlyList<string> KeepsOwn
    ) : WarpSavePlan
    {
        /// <summary>Every added source of the track and whether this save writes it (ADR 0111).</summary>
        public IReadOnlyList<AddedSourcePlan> Added { get; init; } = [];

        /// <summary>Set when no stem holds the warp: the reference added source's warp, on the mix's timeline.</summary>
        public AnalysisFile? MixWarp { get; init; }
    }

    /// <summary>No stem the track is warped by holds a saved warp.</summary>
    public sealed record NoWarp : WarpSavePlan;

    /// <summary>Two stems the track is warped by hold different warps, which is the user's to settle.</summary>
    public sealed record Disagree(IReadOnlyList<string> Stems) : WarpSavePlan;

    public sealed record Failed(string Error) : WarpSavePlan;
}

public abstract record WarpSaveOutcome
{
    private WarpSaveOutcome() { }

    public sealed record Saved(WarpSaved Save) : WarpSaveOutcome;

    /// <summary>A stem was saved over since the draft began, so nothing was written.</summary>
    public sealed record Changed(string Stem) : WarpSaveOutcome;

    public sealed record Failed(string Error) : WarpSaveOutcome;
}

public abstract record WarpUndoOutcome
{
    private WarpUndoOutcome() { }

    public sealed record Undone : WarpUndoOutcome;

    /// <summary>A stem was saved over since, so nothing was put back.</summary>
    public sealed record Changed(string Stem) : WarpUndoOutcome;

    public sealed record Failed(string Error) : WarpUndoOutcome;
}

/// <summary>What a save wrote, with what Undo needs to put it back.</summary>
public sealed record WarpSaved(
    DateTimeOffset At,
    IReadOnlyList<SavedStem> Stems,
    IReadOnlyList<SavedStem> Lent,
    IReadOnlyList<SavedStem> Shifted
);

/// <param name="Backup">The sidecar as it was before the save.</param>
/// <param name="Was">What crateside had recorded of it then, put back by Undo; null when it held the user's own.</param>
/// <param name="After">The sidecar as the save left it.</param>
public sealed record SavedStem(string Path, string Backup, WrittenWarp? Was, SidecarStamp After);

/// <summary>The added source a track kept only as added sources is warped from, with its warp on the mix's timeline.</summary>
public sealed record MixLane(AddedSourceFile Source, AnalysisFile Mix, OffsetReading Reading)
{
    /// <summary>Where the file's first sample falls on the mix's time axis, in seconds.</summary>
    public double Shift => (double)Reading.Offset.GetValueOrDefault() / Reading.SampleRate;
}
