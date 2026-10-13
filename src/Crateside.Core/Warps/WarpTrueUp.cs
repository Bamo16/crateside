using System.Diagnostics;
using AbleKit.Analysis;
using Crateside.Core.Samples;
using Crateside.Core.Tempo;

namespace Crateside.Core.Warps;

/// <summary>
/// Retypes the steady warp a song's canonical stems share to the tempo its transients hold, then
/// lends it again to the stems crateside copied it to (#99). Never touches a warp the user made on
/// another stem.
/// </summary>
public sealed class WarpTrueUp(
    RetypedWarps retyped,
    WrittenWarps written,
    SidecarBackups backups,
    WarpActions actions,
    TimeProvider time
)
{
    private const int SampleRate = 44_100;

    /// <summary>What retyping one song's warp would do, without doing it.</summary>
    public TrueUpPlan Plan(WarpSiblings siblings, SiblingFile song)
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

            return TrueUpDecision.Of(analysis, SampleRate) switch
            {
                TrueUpVerdict.Retype verdict => new TrueUpPlan.Retype(
                    verdict,
                    [.. canonical.Select(stem => Stamped(stem.Cut.Path))],
                    [
                        .. canonical
                            .SelectMany(stem => written.LentTo(stem.Cut.Path))
                            .Distinct(StringComparer.OrdinalIgnoreCase),
                    ]
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

            foreach (var stamp in plan.Sidecars)
            {
                var sidecar = StemFileName.AnalysisFor(stamp.Path);
                var analysis = AnalysisFile.Read(sidecar);

                backups.Keep(sidecar);
                (
                    analysis with
                    {
                        Warp = TrueUpDecision.Retyped(analysis.Warp, plan.Verdict.Bpm),
                    }
                ).Write(sidecar);
                retyped.Record(stamp.Path, plan.Verdict.Bpm, time.GetUtcNow());
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
    public sealed record Retype(
        TrueUpVerdict.Retype Verdict,
        IReadOnlyList<SidecarStamp> Sidecars,
        IReadOnlyList<string> Lent
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
