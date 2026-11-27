using Crateside.Core.Beats;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Warps;

/// <summary>
/// Works out each stem's sidecar while the stems are still in the working set, and writes each one
/// as its stem lands in staging, so a stem never sits there waiting on analysis (#77, ADR 0047).
/// </summary>
public sealed class HandoffSidecars(
    CratesidePaths paths,
    SidecarWriter writer,
    BeatReader beats,
    IOptions<WarpOptions> options,
    KeptWarps? keptWarps = null
)
{
    private readonly WarpOptions _options = options.Value;

    /// <summary>The sidecars to write, by the stem's path in the working set; empty when both switches are off.</summary>
    public async Task<IReadOnlyDictionary<string, PreparedSidecar>> PrepareAsync(
        IReadOnlyList<string> stems,
        CancellationToken ct = default
    )
    {
        if (_options is { LendAutomatically: false, AutoWarps: AutoWarps.Off })
            return new Dictionary<string, PreparedSidecar>();

        var siblings = WarpSiblings.Scan([paths.StagingFolder, paths.LibraryFolder]);
        var cuts = stems.Select(WarpSiblings.Read).OfType<SiblingFile>().ToList();
        AutoWarpGrid? grid = null;
        string? whyNoGrid = null;
        var measured = false;
        Dictionary<string, PreparedSidecar> prepared = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var cut in cuts)
        {
            var authorities = siblings.AuthoritiesFor(cut);
            var sameKind = siblings.SameKindAs(cut);

            // Only when no sibling on disk holds a warp: a re-cut took the ones it had away (ADR 0103).
            if (
                !writer.HasWarpToLend(authorities, sameKind)
                && keptWarps?.Of(cut.SourceTrackKey) is { } kept
            )
            {
                authorities = kept.AuthoritiesFor(cut);
                sameKind = kept.SameKindAs(cut);
            }

            SidecarPlan? plan = null;

            if (writer.HasWarpToLend(authorities, sameKind))
            {
                if (_options.LendAutomatically)
                    plan = await writer.PrepareBorrowAsync(cut.Path, authorities, sameKind, ct);
            }
            else if (_options.AutoWarps is not AutoWarps.Off)
            {
                if (!measured)
                {
                    (grid, whyNoGrid) = await AutoWarpGridAsync(cuts, ct);
                    measured = true;
                }

                plan = grid is not null
                    ? await writer.PrepareAutoWarpAsync(
                        cut.Path,
                        sameKind,
                        grid,
                        WarpSiblings.TempoSources(cuts).FirstOrDefault()?.Path,
                        ct
                    )
                    : await writer.PrepareUnwarpedAsync(cut.Path, sameKind, ct, whyNoGrid);
            }

            if (plan is SidecarPlan.Ready(var sidecar))
                prepared[cut.Path] = sidecar;
        }

        return prepared;
    }

    /// <summary>Writes the stem's prepared sidecar where it landed, and moves its tempo reading with it.</summary>
    public WarpOutcome? Moved(
        IReadOnlyDictionary<string, PreparedSidecar> prepared,
        string from,
        string to
    )
    {
        beats.Moved(from, to);

        return prepared.TryGetValue(from, out var sidecar) ? writer.Commit(sidecar, to) : null;
    }

    /// <summary>The auto warp grid of the upload's reference file when its tempo holds steady; a drifting one gets Warp off instead of an auto warp.</summary>
    private async Task<(AutoWarpGrid? Grid, string? Why)> AutoWarpGridAsync(
        List<SiblingFile> cuts,
        CancellationToken ct
    )
    {
        if (WarpSiblings.TempoSources(cuts).FirstOrDefault() is not { } stem)
            return (null, "no instrumental, kept source or karaoke stem to read a tempo from");

        if (beats.Build is null)
            await beats.RefreshBuildAsync(ct);

        var reading =
            beats.ReadFor(stem.SourceTrackKey, stem.Path)
            ?? await beats.MeasureAsync(beats.ReferenceFor(stem.SourceTrackKey, stem.Path), ct);

        return AutoWarpGrid.Of(reading) is { } grid
            ? (grid, null)
            : (null, AutoWarpGrid.WhyNone(reading));
    }
}
