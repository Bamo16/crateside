using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Verdict;

/// <summary>The composed prompt's size against this machine's real evidence.</summary>
public sealed class VerdictRunnerRealDataTests
{
    /// <summary>A tool call's round trip: the measured per-turn floor, in tokens.</summary>
    private const int RoundTripTokens = 13_000;

    private static readonly WorkingSet Set = new();

    private static VerdictRunner Runner => new(TestLedger.Store(), Set, new CratesidePaths());

    private static WorkOrder Order(string videoId) =>
        WorkOrder.Compose(
            new WorkItem
            {
                PlaylistItemId = $"PLI-{videoId}",
                VideoId = videoId,
                Title = videoId,
            },
            TestLedger.From("does-not-exist")
        );

    private static List<string> Detected => [.. Set.VideoIds.Where(Set.HasEvidence)];

    [Fact]
    public void Real_evidence_is_small_enough_to_carry()
    {
        Assert.SkipWhen(Detected.Count is 0, "no working sets on this machine");

        foreach (var videoId in Detected)
        {
            Assert.True(
                Set.TryReadEvidence(videoId, out var evidence),
                $"{videoId} produced evidence too large to inline"
            );

            // Roughly four characters to the token; the order of magnitude is what matters.
            Assert.True(
                evidence.Length / 4 < RoundTripTokens,
                $"{videoId} evidence is ~{evidence.Length / 4} tokens a turn"
            );
        }
    }

    [Fact]
    public void Every_prompt_carries_its_own_evidence()
    {
        Assert.SkipWhen(Detected.Count is 0, "no working sets on this machine");

        foreach (var videoId in Detected)
        {
            var prompt = Runner.Prompt(Order(videoId));

            Assert.Contains("\"algorithms\"", prompt);
            Assert.Contains("```json", prompt);
        }
    }
}
