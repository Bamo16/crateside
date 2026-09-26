using Crateside.Core.Orchestration.Download;

namespace Crateside.Tests.Orchestration;

/// <summary>Which download outcomes stop a track, and which are simply recorded (ADR 0016).</summary>
public class AcquisitionTests
{
    private static Acquisition Got(PremiumStatus status) => new() { PremiumStatus = status };

    [Theory]
    [InlineData(PremiumStatus.NoPremiumLadder)]
    [InlineData(PremiumStatus.SourceHasNoPremiumAudio)]
    public void A_shortfall_about_the_upload_stops_the_track(PremiumStatus status)
    {
        Assert.True(Got(status).Stops);
        Assert.NotNull(Got(status).Advisory);
    }

    [Theory]
    [InlineData(PremiumStatus.NotSignedIn)]
    [InlineData(PremiumStatus.AccountNotPremium)]
    public void A_shortfall_about_the_session_never_stops_one_track(PremiumStatus status)
    {
        Assert.False(Got(status).Stops);
        Assert.Null(Got(status).Advisory);
    }

    [Theory]
    [InlineData(PremiumStatus.Premium)]
    [InlineData(PremiumStatus.NotApplicable)]
    [InlineData(PremiumStatus.PremiumNotSelected)]
    [InlineData(PremiumStatus.Unreported)]
    public void Nothing_else_stops_anything(PremiumStatus status) =>
        Assert.False(Got(status).Stops);

    [Fact]
    public void The_users_say_so_resolves_the_finding_without_changing_what_was_measured()
    {
        var accepted = Got(PremiumStatus.NoPremiumLadder) with { IsAccepted = true };

        Assert.False(accepted.Stops);
        Assert.Equal(PremiumStatus.NoPremiumLadder, accepted.PremiumStatus);
    }

    [Theory]
    [InlineData("NoPremiumLadder", PremiumStatus.NoPremiumLadder)]
    [InlineData("Premium", PremiumStatus.Premium)]
    [InlineData("SourceHasNoPremiumAudio", PremiumStatus.SourceHasNoPremiumAudio)]
    public void The_status_is_read_by_the_name_stemforge_serializes(
        string reported,
        PremiumStatus expected
    ) => Assert.Equal(expected, Acquisition.StatusFrom(reported));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingStemForgeAddedLater")]
    [InlineData("99")]
    public void An_unreadable_status_is_unreported_rather_than_a_shortfall(string? reported)
    {
        Assert.Equal(PremiumStatus.Unreported, Acquisition.StatusFrom(reported));
        Assert.False(Got(Acquisition.StatusFrom(reported)).Stops);
    }
}
