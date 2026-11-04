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

    private static WorkItem Item(string trackKey) =>
        new()
        {
            PlaylistItemId = $"PLI-{trackKey}",
            TrackKey = trackKey,
            Title = trackKey,
        };

    [Fact]
    public void Every_detected_and_unjudged_track_is_pickable()
    {
        var detected = Set.TrackKeys.Where(Set.HasEvidence).ToList();

        Assert.SkipWhen(detected.Count is 0, "no working sets on this machine");

        var ledger = TestLedger.FromReal();

        // Checked one at a time so a failure names which condition is false.
        foreach (var trackKey in detected)
        {
            var judged = ledger.HasRunFor(trackKey);
            var allowed = StageEligibility.Allows(
                Jobs,
                trackKey,
                JobStage.Verdict,
                VerdictSelector.DefaultMaxStrikes,
                retrySucceeded: true
            );

            // A download short of premium audio stops before the verdict (ADR 0016).
            var stopped = Set.AcquisitionFor(trackKey)?.Stops is true;

            var picked = new VerdictSelector(Jobs, Set).TryPickNext(
                WorkListsView.Compose(
                    [Item(trackKey)],
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
                $"{trackKey}: hasEvidence=true judged={judged} allowed={allowed} "
                    + $"stopped={stopped} picked={picked}"
            );

            if (picked)
                Assert.Equal(trackKey, next.TrackKey);
        }
    }
}
