using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// The composed prompt against this machine's real working sets.
/// </summary>
/// <remarks>
/// <para>
/// The prompt is what the verdict stage actually costs, and the fixture in
/// <see cref="VerdictRunnerTests"/> is <c>{}</c> — which says nothing about what a real
/// <c>keytag-detect</c> run produces. These check the size of the thing being carried against
/// real evidence, because the whole reason to carry it is that it is small.
/// </para>
/// <para>
/// Skips when the machine has no working sets, so it is a no-op on a fresh checkout.
/// </para>
/// </remarks>
public class VerdictRunnerRealDataTests
{
    /// <summary>
    /// What the inlining is bought against: a tool call is a whole round trip, and the measured
    /// per-turn floor after this change is ~13k.
    /// </summary>
    private const int RoundTripTokens = 13_000;

    private static readonly WorkingSet Set = new();

    private static VerdictRunner Runner => new(Set, new CratesidePaths());

    private static WorkOrder Order(string videoId) =>
        WorkOrder.Compose(
            new QueueItem
            {
                PlaylistItemId = $"PLI-{videoId}",
                VideoId = videoId,
                Title = videoId,
            },
            KeyLedger.Read("does-not-exist")
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

            // Roughly four characters to the token. The point is the order of magnitude: this
            // is carried on every turn, and it is bought against one round trip that is not.
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

            // The detector names the file it read, so this is present in every real evidence
            // JSON and absent from the rest of the prompt.
            Assert.Contains("\"algorithms\"", prompt);
            Assert.Contains("```json", prompt);
        }
    }
}
