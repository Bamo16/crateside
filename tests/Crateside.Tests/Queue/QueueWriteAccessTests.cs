using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

public class QueueWriteAccessTests
{
    [Fact]
    public void Nothing_is_said_while_writing_works()
    {
        Assert.False(new QueueWriteAccess().ShouldTell(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void A_lapse_is_said_once_per_interval_rather_than_once()
    {
        var access = new QueueWriteAccess();

        access.Lapsed("invalid_grant");

        Assert.True(access.ShouldTell(TimeSpan.FromMinutes(30)));
        Assert.False(access.ShouldTell(TimeSpan.FromMinutes(30)));

        // Nothing automatic renews the token, so it is worth saying again.
        Assert.True(access.ShouldTell(TimeSpan.Zero));
    }

    [Fact]
    public void The_time_it_lapsed_is_the_first_failure_not_the_latest()
    {
        var access = new QueueWriteAccess();

        access.Lapsed("invalid_grant");

        var first = access.LapsedAt;

        access.Lapsed("invalid_grant again");

        // How long it has been broken is the useful figure; every later attempt fails the same
        // way and would otherwise keep resetting the clock.
        Assert.Equal(first, access.LapsedAt);
    }

    [Fact]
    public void Consent_granted_again_clears_the_lapse_and_the_telling()
    {
        var access = new QueueWriteAccess();

        access.Lapsed("invalid_grant");
        Assert.True(access.ShouldTell(TimeSpan.FromMinutes(30)));

        access.Works();

        Assert.False(access.HasLapsed);
        Assert.Null(access.Reason);
        Assert.False(access.ShouldTell(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void Only_the_entries_that_were_left_behind_are_kept()
    {
        var access = new QueueWriteAccess();

        access.Record([
            new RemovalResult
            {
                PlaylistItemId = "a",
                Title = "Removed",
                Removed = true,
            },
            new RemovalResult
            {
                PlaylistItemId = "b",
                Title = "Light Years Away",
                Removed = false,
                Reason = "forbidden: the authorized account cannot modify this playlist.",
            },
        ]);

        var left = Assert.Single(access.Failures);

        Assert.Equal("Light Years Away", left.Title);
        Assert.NotNull(left.Reason);
    }

    [Fact]
    public void A_clean_run_leaves_nothing_behind()
    {
        var access = new QueueWriteAccess();

        access.Record([
            new RemovalResult
            {
                PlaylistItemId = "b",
                Title = "Light Years Away",
                Removed = false,
            },
        ]);
        access.Record([
            new RemovalResult
            {
                PlaylistItemId = "a",
                Title = "Removed",
                Removed = true,
            },
        ]);

        // Replaced, not accumulated: the list means "what the last consume could not remove".
        Assert.Empty(access.Failures);
    }
}
