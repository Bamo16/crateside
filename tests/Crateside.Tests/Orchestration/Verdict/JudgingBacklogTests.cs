using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class JudgingBacklogTests
{
    [Theory]
    [InlineData(0, 0, 0, "0 waiting")]
    [InlineData(4, 0, 0, "4 waiting")]
    [InlineData(0, 50, 8, "50 re-judges, 8 backfill waiting")]
    [InlineData(0, 1, 0, "1 re-judge waiting")]
    [InlineData(0, 0, 8, "8 backfill waiting")]
    [InlineData(3, 50, 8, "3 new, 50 re-judges, 8 backfill waiting")]
    public void The_count_names_what_is_waiting_and_skips_what_is_not(
        int intake,
        int rejudges,
        int backfill,
        string expected
    ) => Assert.Equal(expected, new JudgingBacklog(intake, rejudges, backfill).Describe());

    [Fact]
    public void Intake_alone_is_not_more_than_intake() =>
        Assert.False(new JudgingBacklog(4, 0, 0).HasMoreThanIntake);

    [Fact]
    public void A_re_judge_or_backfill_track_is_more_than_intake()
    {
        Assert.True(new JudgingBacklog(0, 1, 0).HasMoreThanIntake);
        Assert.True(new JudgingBacklog(0, 0, 1).HasMoreThanIntake);
    }
}
