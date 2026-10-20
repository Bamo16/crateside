using Crateside.Core.Samples;
using Crateside.Core.Tempo;

namespace Crateside.Core.Warps;

/// <summary>Plans a true-up for every song in staging and the library at once, as the warp view plans one, and applies them (#96).</summary>
public sealed class TrueUpAll(CratesidePaths paths, WarpTrueUp trueUp)
{
    public TrueUpBatch Plan(CancellationToken ct = default)
    {
        string[] audio =
        [
            .. Audio(paths.LibraryFolder, SearchOption.AllDirectories),
            .. Audio(paths.StagingFolder, SearchOption.TopDirectoryOnly),
            .. Audio(paths.KeptWarpGuides, SearchOption.TopDirectoryOnly),
        ];
        var siblings = WarpSiblings.Of(audio);
        List<TrueUpPlan.Retype> plans = [];
        var refused = 0;

        foreach (
            var cuts in audio
                .Select(WarpSiblings.Read)
                .OfType<SiblingFile>()
                .GroupBy(cut => cut.SourceVideoId, StringComparer.Ordinal)
        )
        {
            ct.ThrowIfCancellationRequested();

            switch (
                trueUp.Plan(
                    siblings,
                    cuts.OrderByDescending(cut => cut.IsAuthority).First(),
                    rephase: true
                )
            )
            {
                case TrueUpPlan.Retype plan:
                    plans.Add(plan);

                    break;

                case TrueUpPlan.Left(TrueUpReason.DoesNotHold):
                    refused++;

                    break;
            }
        }

        return new TrueUpBatch(siblings, plans, refused);
    }

    /// <summary>Applies each plan in turn; a song saved over since the plan is skipped, as one true-up skips it.</summary>
    public async Task<IReadOnlyList<TrueUpDone>> ApplyAsync(
        TrueUpBatch batch,
        CancellationToken ct = default
    )
    {
        List<TrueUpDone> done = [];

        foreach (var plan in batch.Plans)
        {
            ct.ThrowIfCancellationRequested();

            done.Add(
                new TrueUpDone(
                    StemFileName.WithoutPreset(plan.Sidecars[0].Path),
                    await trueUp.ApplyAsync(plan, batch.Siblings, ct)
                )
            );
        }

        return done;
    }

    private static IEnumerable<string> Audio(string root, SearchOption depth) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", depth).Where(SampleFiles.IsAudio)
            : [];
}

/// <param name="Refused">Songs a retype was found for whose grid would then not hold through the track.</param>
public sealed record TrueUpBatch(
    WarpSiblings Siblings,
    IReadOnlyList<TrueUpPlan.Retype> Plans,
    int Refused
);

public sealed record TrueUpDone(string Song, TrueUpOutcome Outcome);
