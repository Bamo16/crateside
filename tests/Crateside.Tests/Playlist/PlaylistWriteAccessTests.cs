using Crateside.Core.Playlist;

namespace Crateside.Tests.Playlist;

public sealed class QueueWriteAccessTests
{
    [Fact]
    public void Nothing_is_said_while_writing_works()
    {
        Assert.False(new PlaylistWriteAccess().ShouldTell(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void A_lapse_is_said_once_per_interval_rather_than_once()
    {
        var access = new PlaylistWriteAccess();

        access.Lapsed("invalid_grant");

        Assert.True(access.ShouldTell(TimeSpan.FromMinutes(30)));
        Assert.False(access.ShouldTell(TimeSpan.FromMinutes(30)));

        Assert.True(access.ShouldTell(TimeSpan.Zero));
    }

    [Fact]
    public void The_time_it_lapsed_is_the_first_failure_not_the_latest()
    {
        var access = new PlaylistWriteAccess();

        access.Lapsed("invalid_grant");

        var first = access.LapsedAt;

        access.Lapsed("invalid_grant again");

        Assert.Equal(first, access.LapsedAt);
    }

    [Fact]
    public void Consent_granted_again_clears_the_lapse_and_the_telling()
    {
        var access = new PlaylistWriteAccess();

        access.Lapsed("invalid_grant");
        Assert.True(access.ShouldTell(TimeSpan.FromMinutes(30)));

        access.Works();

        Assert.False(access.HasLapsed);
        Assert.Null(access.Reason);
        Assert.False(access.ShouldTell(TimeSpan.FromMinutes(30)));
    }
}
