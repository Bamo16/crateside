using Crateside.Core.Orchestration.Verdict;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class JudgeBackoffTests
{
    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-10-30T15:36:00+00:00")
    );

    private JudgeBackoff Backoff => field ??= new JudgeBackoff(_time);

    /// <summary>Refuses once and returns how long the hold lasts from now.</summary>
    private TimeSpan Refuse(TimeSpan? retryAfter = null) =>
        Backoff.Refused("rate limited", retryAfter).RetryAt!.Value - _time.GetUtcNow();

    [Fact]
    public void Refusals_that_state_no_wait_hold_a_minute_doubling_to_thirty()
    {
        var waits = Enumerable.Range(0, 8).Select(_ => Refuse().TotalMinutes).ToList();

        Assert.Equal([1, 2, 4, 8, 16, 30, 30, 30], waits);
    }

    [Fact]
    public void A_stated_wait_is_honoured_as_given()
    {
        Refuse();
        Refuse();

        Assert.Equal(TimeSpan.FromSeconds(69), Refuse(TimeSpan.FromSeconds(69)));
    }

    [Fact]
    public void A_stated_wait_still_counts_toward_the_next_unstated_one()
    {
        Refuse(TimeSpan.FromSeconds(900));

        Assert.Equal(TimeSpan.FromMinutes(2), Refuse());
    }

    [Fact]
    public void A_served_session_clears_the_hold_and_starts_the_waits_again()
    {
        Refuse();
        Refuse();
        Refuse();

        Backoff.Served();

        Assert.Null(Backoff.Hold);
        Assert.Equal(TimeSpan.FromMinutes(1), Refuse());
    }

    [Fact]
    public void The_hold_stands_until_its_time_and_lifts_by_itself()
    {
        Refuse();

        _time.Advance(TimeSpan.FromSeconds(59));

        Assert.Equal("rate limited", Backoff.Hold?.Reason);

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(Backoff.Hold);
    }
}
