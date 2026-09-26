using Crateside.Core.Quota;

namespace Crateside.Tests.Quota;

public class WindowRollTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly QuotaSnapshot Idle = new() { Utilization = 0 };

    private static readonly QuotaSnapshot InUse = new() { Utilization = 3 };

    private static WindowRoll Enabled() => new(enabled: true);

    [Fact]
    public void It_starts_off()
    {
        var roll = new WindowRoll();

        Assert.False(roll.IsEnabled);
        Assert.False(roll.ShouldRoll(Idle, Start));
        Assert.False(roll.ShouldRoll(Idle, Start + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void An_unreadable_window_is_never_idle()
    {
        var roll = Enabled();

        Assert.False(roll.ShouldRoll(QuotaSnapshot.Unknown, Start));
        Assert.False(roll.ShouldRoll(QuotaSnapshot.Unknown, Start + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void A_window_that_has_just_read_zero_is_not_yet_idle()
    {
        var roll = Enabled();

        Assert.False(roll.ShouldRoll(Idle, Start));
        Assert.False(roll.ShouldRoll(Idle, Start + WindowRoll.IdleFor - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_window_left_at_zero_for_the_idle_period_rolls()
    {
        var roll = Enabled();

        roll.ShouldRoll(Idle, Start);

        Assert.True(roll.ShouldRoll(Idle, Start + WindowRoll.IdleFor));
    }

    [Fact]
    public void Any_use_restarts_the_idle_clock()
    {
        var roll = Enabled();

        roll.ShouldRoll(Idle, Start);
        roll.ShouldRoll(InUse, Start + TimeSpan.FromMinutes(10));

        Assert.False(roll.ShouldRoll(Idle, Start + TimeSpan.FromMinutes(20)));
        Assert.True(roll.ShouldRoll(Idle, Start + TimeSpan.FromMinutes(35)));
    }

    [Fact]
    public void A_window_in_use_never_rolls()
    {
        var roll = Enabled();

        roll.ShouldRoll(InUse, Start);

        Assert.False(roll.ShouldRoll(InUse, Start + TimeSpan.FromHours(4)));
    }

    [Fact]
    public void One_roll_per_window_even_if_the_verdict_spent_nothing()
    {
        var roll = Enabled();

        roll.ShouldRoll(Idle, Start);
        roll.MarkRolled(Start + WindowRoll.IdleFor);

        var later = Start + WindowRoll.IdleFor;

        roll.ShouldRoll(Idle, later + TimeSpan.FromMinutes(1));

        Assert.False(roll.ShouldRoll(Idle, later + TimeSpan.FromHours(4)));

        roll.ShouldRoll(Idle, later + WindowRoll.Window - WindowRoll.IdleFor);

        Assert.True(roll.ShouldRoll(Idle, later + WindowRoll.Window));
    }

    [Fact]
    public void A_roll_from_before_a_restart_still_holds_its_window()
    {
        var roll = new WindowRoll(enabled: true, rolledAt: Start);
        var idleFrom = Start + TimeSpan.FromHours(1);

        roll.ShouldRoll(Idle, idleFrom);

        Assert.False(roll.ShouldRoll(Idle, idleFrom + WindowRoll.IdleFor));
        Assert.True(roll.ShouldRoll(Idle, Start + WindowRoll.Window));
    }

    [Fact]
    public void Turning_it_off_forgets_the_idle_clock()
    {
        var roll = Enabled();

        roll.ShouldRoll(Idle, Start);
        roll.Disable();
        roll.Enable();

        Assert.False(roll.ShouldRoll(Idle, Start + WindowRoll.IdleFor));
    }

    [Fact]
    public void Changes_are_announced()
    {
        var roll = new WindowRoll();
        var told = 0;

        roll.Changed += () => told++;

        roll.Enable();
        roll.Enable();
        roll.MarkRolled(Start);
        roll.Disable();

        Assert.Equal(3, told);
    }
}
