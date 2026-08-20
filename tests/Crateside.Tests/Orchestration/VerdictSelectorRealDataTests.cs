using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// The verdict selector against this machine's real working set, job store and ledger.
/// </summary>
/// <remarks>
/// Written after the stage sat idle with a track that appeared to satisfy every one of its
/// conditions. Reasoning about it from outside produced two wrong diagnoses, so this asks the
/// code instead — the same reason the other real-data tests here exist. Skips when the machine
/// has no working sets, so it is a no-op on a fresh checkout.
/// </remarks>
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

        // The stage's own entry and exit conditions, checked one at a time so a failure names
        // which of them is actually false rather than just "nothing was picked".
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

            // Detected, unjudged and not given up on == pickable. Stating all three is what
            // makes a failure name the reason: the bug this was written for had every condition
            // satisfied and still picked nothing.
            Assert.True(
                picked == (!judged && allowed),
                $"{videoId}: hasEvidence=true judged={judged} allowed={allowed} picked={picked}"
            );

            if (picked)
                Assert.Equal(videoId, next.VideoId);
        }
    }
}
