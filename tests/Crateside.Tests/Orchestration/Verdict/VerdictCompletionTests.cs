using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class VerdictCompletionTests
{
    private static RunRecord Run(string? reasoning) =>
        new()
        {
            RecordedAt = DateTimeOffset.UnixEpoch,
            Song = "artist - title",
            Confidence = KeyConfidence.High,
            Reasoning = reasoning,
        };

    [Fact]
    public void A_note_is_added_to_the_reasoning_as_its_own_paragraph()
    {
        var run = VerdictCompletion.WithNote(Run("Backed by both."), "crateside capped this.");

        Assert.Equal("Backed by both.\n\ncrateside capped this.", run.Reasoning);
    }

    [Fact]
    public void A_note_stands_alone_where_the_run_gave_no_reasoning()
    {
        var run = VerdictCompletion.WithNote(Run(null), "crateside capped this.");

        Assert.Equal("crateside capped this.", run.Reasoning);
    }

    [Fact]
    public void No_note_leaves_the_run_as_it_was()
    {
        var run = Run("Backed by both.");

        Assert.Same(run, VerdictCompletion.WithNote(run, null));
    }
}
