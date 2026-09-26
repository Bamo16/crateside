using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Orchestration;

/// <summary>The verdict selector against this machine's real working set, job store and ledger.</summary>
public class VerdictSelectorRealDataTests
{
    private static readonly WorkingSet Set = new();
    private static readonly JobStore Jobs = new();
    private static readonly CratesidePaths Paths = new();

    private static QueueItem Item(string videoId) =>
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

        var ledger = KeyLedger.Read(Paths.Ledger);

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
                QueueView.Compose(
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
