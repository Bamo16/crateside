using System.Diagnostics;
using AbleKit.Analysis;
using Crateside.Core.Ableton;
using Crateside.Core.Samples;
using Crateside.Core.Tempo;

namespace Crateside.Core.Warps;

/// <summary>
/// Retypes the steady warp a song's canonical stems share to the tempo its transients hold, with any
/// stem the user saved with the same markers, then lends it again to the stems crateside copied it to
/// (#99). Never touches a warp of the user's that differs.
/// </summary>
public sealed class WarpTrueUp(
    RetypedWarps retyped,
    WrittenWarps written,
    SidecarBackups backups,
    WarpActions actions,
    TimeProvider time
)
{
    private const int DefaultSampleRate = 44_100;

    /// <summary>How far any 30 s of the track may sit from its grid after a rephase, in ms.</summary>
    public const double HoldsWithinMs = 5;

    private const double BlockSeconds = 30;
    private const int MinWindowsToPhase = 6;

    /// <summary>
    /// What retyping one song's warp would do, without doing it. With <paramref name="rephase"/> the
    /// warp is also moved so the drum stem's transients, else the canonical stem's, sit on its grid.
    /// </summary>
    public TrueUpPlan Plan(WarpSiblings siblings, SiblingFile song, bool rephase = false)
    {
        try
        {
            var canonical = siblings
                .CutsOf(song)
                .Where(cut => cut.IsAuthority && !written.IsCratesides(cut.Path))
                .Select(cut => (Cut: cut, Analysis: Saved(cut.Path)))
                .Where(stem => stem.Analysis is not null)
                .ToArray();

            if (canonical is [])
                return new TrueUpPlan.Left(TrueUpReason.NoWarp);

            if (canonical.Skip(1).Any(stem => !SameWarp(stem.Analysis!, canonical[0].Analysis!)))
                return new TrueUpPlan.Disagree([.. canonical.Select(stem => stem.Cut.Path)]);

            var source = WarpSiblings.TempoSources(canonical.Select(stem => stem.Cut)).First();
            var analysis = canonical.First(stem => stem.Cut == source).Analysis!;

            // A vocal the user saved with the track's warp is usually the same markers; it moves too.
            var sharing = siblings
                .CutsOf(song)
                .Where(cut =>
                    !cut.IsAuthority
                    && !written.IsCratesides(cut.Path)
                    && Saved(cut.Path) is { } other
                    && other.Warp.Markers.SequenceEqual(analysis.Warp.Markers)
                )
                .Select(cut => cut.Path);

            var rate = RateOf(source.Path);

            return TrueUpDecision.Of(analysis, rate) switch
            {
                TrueUpVerdict.Retype verdict => Retype(
                    verdict,
                    canonical.Select(stem => stem.Cut.Path).Concat(sharing),
                    rephase
                        ? Phase(siblings, song, analysis, source.Path, verdict.Bpm, rate)
                        : (0, null)
                ),
                TrueUpVerdict.Leave(var reason) => new TrueUpPlan.Left(reason),
                _ => throw new UnreachableException(),
            };
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new TrueUpPlan.Failed(ex.Message);
        }
    }

    private TrueUpPlan Retype(
        TrueUpVerdict.Retype verdict,
        IEnumerable<string> canonical,
        (double Shift, string? From)? phase
    ) =>
        phase is not var (shift, from)
            ? new TrueUpPlan.Left(TrueUpReason.DoesNotHold)
            : new TrueUpPlan.Retype(
                verdict,
                [.. canonical.Select(Stamped)],
                [
                    .. canonical
                        .SelectMany(written.LentTo)
                        .Distinct(StringComparer.OrdinalIgnoreCase),
                ],
                shift,
                from
            );

    /// <summary>
    /// How far later the retyped warp has to sit for the transients of the kept drum stem, else the
    /// canonical stem, to centre on its grid; null when the grid then does not hold within 5 ms
    /// through every 30 s.
    /// </summary>
    private static (double Shift, string? From)? Phase(
        WarpSiblings siblings,
        SiblingFile song,
        AnalysisFile canonical,
        string canonicalPath,
        double bpm,
        int rate
    )
    {
        var (times, from) = PhaseSource(siblings, song, canonical, canonicalPath, rate);
        var warp = TrueUpDecision.Retyped(canonical.Warp, bpm);
        var offset = GridCheck.Against(times, warp);

        if (offset.Windows.Count < MinWindowsToPhase)
            return null;

        var shift = offset.MedianOffsetMs / 1000;

        return GridCheck
            .Against(times, TrueUpDecision.Retyped(canonical.Warp, bpm, shift))
            .OffsetsPer(BlockSeconds)
            .All(ms => Math.Abs(ms) <= HoldsWithinMs)
            ? (shift, from == canonicalPath ? null : from)
            : null;
    }

    private static (double[] Times, string From) PhaseSource(
        WarpSiblings siblings,
        SiblingFile song,
        AnalysisFile canonical,
        string canonicalPath,
        int rate
    )
    {
        foreach (var drums in siblings.CutsOf(song).Where(cut => cut.Kind is "Drums"))
        {
            if (
                AnalysisFile.TryRead(StemFileName.AnalysisFor(drums.Path), out var analysis)
                && analysis.Audio.Transients is { Count: > 0 } transients
            )
                return (GridCheck.Strongest(transients, RateOf(drums.Path)), drums.Path);
        }

        return (GridCheck.Strongest(canonical.Audio.Transients, rate), canonicalPath);
    }

    /// <summary>The canonical stem's strongest transients against the grid its warp lays down, for the warp view's Off the grid row (#96).</summary>
    public static GridReport? Check(StemWarp lender) =>
        lender.Sidecar is { Warp.Markers.Count: > 1 } sidecar
            ? GridCheck.Against(
                GridCheck.Strongest(sidecar.Audio.Transients, RateOf(lender.Path)),
                sidecar.Warp
            )
            : null;

    private static int RateOf(string audioPath) =>
        WarpTempoReader.SampleRateOf(audioPath) ?? DefaultSampleRate;

    /// <summary>
    /// Writes the plan: each canonical sidecar backed up and given the new tempo, if it is still as
    /// it was planned, then every copy crateside lent from them written again.
    /// </summary>
    public async Task<TrueUpOutcome> ApplyAsync(
        TrueUpPlan.Retype plan,
        WarpSiblings siblings,
        CancellationToken ct = default
    )
    {
        try
        {
            if (plan.Sidecars.Any(stamp => Stamped(stamp.Path) != stamp))
                return new TrueUpOutcome.Changed();

            List<(string Sidecar, string Backup)> written = [];

            try
            {
                foreach (var stamp in plan.Sidecars)
                {
                    if (Stamped(stamp.Path) != stamp)
                        throw new TrueUpChangedException();

                    var sidecar = StemFileName.AnalysisFor(stamp.Path);
                    var analysis = AnalysisFile.Read(sidecar);
                    var backup = backups.Keep(sidecar);

                    (
                        analysis with
                        {
                            Warp = TrueUpDecision.Retyped(
                                analysis.Warp,
                                plan.Verdict.Bpm,
                                plan.ShiftSeconds
                            ),
                        }
                    ).Write(sidecar);
                    written.Add((sidecar, backup));
                    retyped.Record(stamp.Path, plan.Verdict.Bpm, time.GetUtcNow());
                }
            }
            catch (Exception ex)
                when (ex is TrueUpChangedException or IOException or UnauthorizedAccessException)
            {
                foreach (var (sidecar, backup) in written)
                    File.Copy(backup, sidecar, overwrite: true);

                return ex is TrueUpChangedException
                    ? new TrueUpOutcome.Changed()
                    : new TrueUpOutcome.Failed(ex.Message);
            }

            List<(string Path, WarpOutcome Outcome)> lent = [];

            foreach (var path in plan.Lent)
            {
                ct.ThrowIfCancellationRequested();

                if (WarpSiblings.Read(path) is not { } copy)
                    continue;

                lent.Add(
                    (
                        path,
                        await actions.ReapplyAsync(
                            path,
                            siblings.AuthoritiesFor(copy),
                            siblings.SameKindAs(copy),
                            ct
                        )
                    )
                );
            }

            return new TrueUpOutcome.Retyped(plan.Verdict.Bpm, lent);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new TrueUpOutcome.Failed(ex.Message);
        }
    }

    private static AnalysisFile? Saved(string audioPath) =>
        AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
        && analysis.IsDefaultClipSaved
            ? analysis
            : null;

    private static SidecarStamp Stamped(string audioPath)
    {
        var sidecar = new FileInfo(StemFileName.AnalysisFor(audioPath));

        return new SidecarStamp(audioPath, sidecar.Length, sidecar.LastWriteTimeUtc);
    }

    private static bool SameWarp(AnalysisFile left, AnalysisFile right) =>
        left.DefaultClip == right.DefaultClip
        && left.Warp.Markers.SequenceEqual(right.Warp.Markers);
}

/// <summary>A sidecar as it was when the plan read it.</summary>
public readonly record struct SidecarStamp(string Path, long Length, DateTime Written);

public abstract record TrueUpPlan
{
    private TrueUpPlan() { }

    /// <param name="Sidecars">The canonical stems whose sidecars are rewritten.</param>
    /// <param name="Lent">The stems crateside copied their warp to, written again afterwards.</param>
    /// <param name="ShiftSeconds">How much later every marker moves to sit on the transients; 0 where the warp is only retyped.</param>
    /// <param name="PhasedFrom">The kept drum stem the phase was read from, or null when it was the canonical stem or there is no phase.</param>
    public sealed record Retype(
        TrueUpVerdict.Retype Verdict,
        IReadOnlyList<SidecarStamp> Sidecars,
        IReadOnlyList<string> Lent,
        double ShiftSeconds = 0,
        string? PhasedFrom = null
    ) : TrueUpPlan;

    public sealed record Left(TrueUpReason Reason) : TrueUpPlan;

    /// <summary>Two canonical stems hold different warps, which is the user's to settle.</summary>
    public sealed record Disagree(IReadOnlyList<string> Stems) : TrueUpPlan;

    public sealed record Failed(string Error) : TrueUpPlan;
}

public abstract record TrueUpOutcome
{
    private TrueUpOutcome() { }

    public sealed record Retyped(double Bpm, IReadOnlyList<(string Path, WarpOutcome Outcome)> Lent)
        : TrueUpOutcome;

    /// <summary>A canonical sidecar was saved over since the plan, so nothing was written.</summary>
    public sealed record Changed : TrueUpOutcome;

    public sealed record Failed(string Error) : TrueUpOutcome;
}

/// <summary>A canonical sidecar was saved over between being checked and being written.</summary>
internal sealed class TrueUpChangedException : Exception;
