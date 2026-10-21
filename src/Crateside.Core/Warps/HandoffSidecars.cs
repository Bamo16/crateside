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
    IOptions<WarpOptions> options
)
{
    private readonly WarpOptions _options = options.Value;

    /// <summary>The sidecars to write, by the stem's path in the working set; empty when both switches are off.</summary>
    public async Task<IReadOnlyDictionary<string, PreparedSidecar>> PrepareAsync(
        IReadOnlyList<string> stems,
        CancellationToken ct = default
    )
    {
        if (_options is { LendAutomatically: false, StarterWarps: StarterWarps.Off })
            return new Dictionary<string, PreparedSidecar>();

        var siblings = WarpSiblings.Scan([paths.StagingFolder, paths.LibraryFolder]);
        var cuts = stems.Select(WarpSiblings.Read).OfType<SiblingFile>().ToList();
        StarterGrid? grid = null;
        var measured = false;
        Dictionary<string, PreparedSidecar> prepared = new(StringComparer.OrdinalIgnoreCase);

        foreach (var cut in cuts)
        {
            var authorities = siblings.AuthoritiesFor(cut);
            var sameKind = siblings.SameKindAs(cut);
            SidecarPlan? plan = null;

            if (writer.HasWarpToLend(authorities, sameKind))
            {
                if (_options.LendAutomatically)
                    plan = await writer.PrepareBorrowAsync(cut.Path, authorities, sameKind, ct);
            }
            else if (_options.StarterWarps is not StarterWarps.Off)
            {
                if (!measured)
                {
                    grid = await StarterGridAsync(cuts, ct);
                    measured = true;
                }

                plan = grid is not null
                    ? await writer.PrepareStarterAsync(
                        cut.Path,
                        sameKind,
                        grid,
                        WarpSiblings.TempoSources(cuts).FirstOrDefault()?.Path,
                        ct
                    )
                    : await writer.PrepareUnwarpedAsync(cut.Path, sameKind, ct);
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

    /// <summary>The starter grid of the batch's instrumental when its tempo holds steady; a drifting one gets Warp off instead of a starter.</summary>
    private async Task<StarterGrid?> StarterGridAsync(List<SiblingFile> cuts, CancellationToken ct)
    {
        if (WarpSiblings.TempoSources(cuts).FirstOrDefault() is not { } instrumental)
            return null;

        if (beats.Build is null)
            await beats.RefreshBuildAsync(ct);

        var reading =
            beats.Read(instrumental.Path) ?? await beats.MeasureAsync(instrumental.Path, ct);

        return StarterGrid.Of(reading);
    }
}
