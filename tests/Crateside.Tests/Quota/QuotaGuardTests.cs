using Crateside.Core.Quota;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Quota;

/// <summary>Judging stops at 85% of the weekly limit and resumes by itself.</summary>
public sealed class QuotaGuardTests
{
    private static readonly DateTimeOffset Reset = new(2026, 10, 19, 4, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    );

    private static QuotaSnapshot Weekly(double used) =>
        QuotaReader.Parse(
            $$"""
            {
              "five_hour": { "utilization": 10.0, "resets_at": "2026-10-14T15:00:00+00:00" },
              "seven_day": { "utilization": {{used.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            )}}, "resets_at": "2026-10-19T04:00:00+00:00" }
            }
            """
        );

    [Fact]
    public void Eighty_four_percent_holds_nothing() =>
        Assert.Null(QuotaGuard.HoldOf(Weekly(84), _time.GetUtcNow()));

    [Fact]
    public void Eighty_five_percent_holds_until_the_weekly_reset()
    {
        var hold = QuotaGuard.HoldOf(Weekly(85), _time.GetUtcNow());

        Assert.Equal(Reset, hold?.ResumesAt);
        Assert.Contains("85%", hold?.Reason);
    }

    [Fact]
    public void The_hold_lifts_when_the_reset_passes_even_if_the_reading_is_old()
    {
        var quota = Weekly(90);

        _time.SetUtcNow(Reset.AddMinutes(1));

        Assert.Null(QuotaGuard.HoldOf(quota, _time.GetUtcNow()));
    }

    [Fact]
    public async Task The_hold_lifts_when_a_later_reading_drops_below_the_limit()
    {
        var readings = new Queue<QuotaSnapshot>([Weekly(90), Weekly(20)]);
        QuotaGuard guard = new(_ => Task.FromResult(readings.Dequeue()), _time);

        Assert.NotNull(await guard.CheckAsync(TestContext.Current.CancellationToken));

        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await guard.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_reading_that_fails_leaves_the_hold_standing()
    {
        var readings = new Queue<QuotaSnapshot>([Weekly(90), QuotaSnapshot.Unknown]);
        QuotaGuard guard = new(_ => Task.FromResult(readings.Dequeue()), _time);

        await guard.CheckAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.NotNull(await guard.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_recent_reading_is_not_read_again()
    {
        var reads = 0;
        QuotaGuard guard = new(
            _ =>
            {
                reads++;

                return Task.FromResult(Weekly(10));
            },
            _time
        );

        await guard.CheckAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(1));
        await guard.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_pass_names_one_track_and_a_second_replaces_it()
    {
        QuotaGuard guard = new(_ => Task.FromResult(Weekly(90)), _time);

        guard.Pass("first");
        guard.Pass("second");

        Assert.Equal("second", guard.PassedVideoId);

        guard.ClearPass();

        Assert.Null(guard.PassedVideoId);
    }
}
