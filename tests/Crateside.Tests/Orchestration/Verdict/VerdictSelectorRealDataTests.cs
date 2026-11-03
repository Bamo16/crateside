using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Verdict;

/// <summary>The verdict selector against this machine's real working set, job store and ledger.</summary>
public sealed class VerdictSelectorRealDataTests
{
    private static readonly WorkingSet Set = new(new CratesidePaths());
    private static readonly JobStore Jobs = new(new CratesidePaths());
    private static readonly CratesidePaths Paths = RealPaths.Create();

    private static WorkItem Item(string videoId) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = videoId,
        };

    [Fact]
    public void Every_detected_and_unjudged_track_is_pickable()
    {
        var detected = Set.VideoIds.Where(Set.HasEvidence).ToList();

        Assert.SkipWhen(detected.Count is 0, "no working sets on this machine");

        var ledger = TestLedger.FromReal();

        // Checked one at a time so a failure names which condition is false.
        foreach (var videoId in detected)
        {
            var judged = ledger.HasRunFor(videoId);
            var allowed = StageEligibility.Allows(
                Jobs,
                videoId,
                JobStage.Verdict,
                VerdictSelector.DefaultMaxStrikes,
                retrySucceeded: true
            );

            // A download short of premium audio stops before the verdict (ADR 0016).
            var stopped = Set.AcquisitionFor(videoId)?.Stops is true;

            var picked = new VerdictSelector(Jobs, Set).TryPickNext(
                WorkListsView.Compose(
                    [Item(videoId)],
                    new HashSet<string>(),
                    new HashSet<string>(),
                    new HashSet<string>(),
                    new Dictionary<string, TrackInstruction>()
                ),
                ledger,
                out var next
            );

            Assert.True(
                picked == (!judged && allowed && !stopped),
                $"{videoId}: hasEvidence=true judged={judged} allowed={allowed} "
                    + $"stopped={stopped} picked={picked}"
            );

            if (picked)
                Assert.Equal(videoId, next.VideoId);
        }
    }
}
