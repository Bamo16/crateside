using Crateside.Core;
using Crateside.Core.KeyDetection;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Verdict;

/// <summary>The composed prompt's size against this machine's real evidence.</summary>
public sealed class VerdictRunnerRealDataTests
{
    /// <summary>A tool call's round trip: the measured per-turn floor, in tokens.</summary>
    private const int RoundTripTokens = 13_000;

    private static readonly WorkingSet Set = new(new CratesidePaths());

    private static VerdictRunner Runner =>
        new(
            TestLedger.Store(),
            Set,
            new SourceIndex(Set, new CratesidePaths()),
            new ProcessRunner(),
            new EvidenceRunner(
                Set,
                DetectorSet.For(new ProcessRunner(), new CratesidePaths().KeydetectFfmpeg)
            ),
            new SourcePages(new FixedHttpClientFactory(new HttpClient())),
            Options.Create(new JudgeOptions()),
            TimeProvider.System,
            NullLogger<VerdictRunner>.Instance
        );

    private static WorkOrder Order(string trackKey) =>
        WorkOrder.Compose(
            new WorkItem
            {
                PlaylistItemId = $"PLI-{trackKey}",
                TrackKey = trackKey,
                Title = trackKey,
            },
            TestLedger.From("does-not-exist")
        );

    private static List<string> Detected => [.. Set.TrackKeys.Where(Set.HasEvidence)];

    [Fact]
    public void Real_evidence_is_small_enough_to_carry()
    {
        Assert.SkipWhen(Detected.Count is 0, "no working sets on this machine");

        foreach (var trackKey in Detected)
        {
            Assert.True(
                Set.TryReadEvidence(trackKey, out var evidence),
                $"{trackKey} produced evidence too large to inline"
            );

            // Roughly four characters to the token; the order of magnitude is what matters.
            Assert.True(
                evidence.Length / 4 < RoundTripTokens,
                $"{trackKey} evidence is ~{evidence.Length / 4} tokens a turn"
            );
        }
    }

    [Fact]
    public void Every_prompt_carries_its_own_evidence()
    {
        Assert.SkipWhen(Detected.Count is 0, "no working sets on this machine");

        foreach (var trackKey in Detected)
        {
            var prompt = Runner.Prompt(Order(trackKey), VerdictRunner.VerdictFileName, []);

            Assert.Contains("\"algorithms\"", prompt);
            Assert.Contains("```json", prompt);
        }
    }
}
