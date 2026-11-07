using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class RunEvidenceFirstPassTests
{
    private static RunEvidence Run(string? withheld, params RunWindow[] windows) =>
        new()
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            WithheldReasoning = withheld,
            Windows = windows,
        };

    private static RunWindow Asked(double start, string? why) =>
        new(start, start + 10, null, WindowAsker.Judge) { Why = why };

    [Fact]
    public void An_older_run_whose_windows_all_carry_the_same_reasoning_shows_it_once_as_the_first_pass()
    {
        var run = Run(null, Asked(0, "the whole argument"), Asked(20, "the whole argument"));

        Assert.Equal("the whole argument", run.FirstPass);
        Assert.Null(run.WhyOf(run.Windows[0]));
    }

    [Fact]
    public void A_newer_run_keeps_the_first_pass_on_the_run_and_each_windows_own_why()
    {
        var run = Run(
            "it asked for windows",
            Asked(0, "settle the chorus"),
            Asked(20, "settle the bridge")
        );

        Assert.Equal("it asked for windows", run.FirstPass);
        Assert.Equal("settle the chorus", run.WhyOf(run.Windows[0]));
    }

    [Fact]
    public void A_window_the_user_gave_has_no_why_of_the_judges()
    {
        var run = Run(null, new RunWindow(0, 10, "chorus", WindowAsker.User) { Why = "stray" });

        Assert.Null(run.WhyOf(run.Windows[0]));
        Assert.Null(run.FirstPass);
    }
}
