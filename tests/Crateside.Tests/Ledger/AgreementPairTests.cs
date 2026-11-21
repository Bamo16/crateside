using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class AgreementPairTests
{
    [Theory]
    [InlineData("madmom+ffmpeg", "madmom and keydetect")]
    [InlineData("edma+ffmpeg", "Essentia and keydetect")]
    [InlineData("madmom+edma", "madmom and Essentia")]
    [InlineData("crema+ffmpeg", "CREMA and keydetect")]
    [InlineData("librosa+crema", "librosa and CREMA")]
    public void A_pair_is_named_by_the_detectors_as_the_page_calls_them(
        string pair,
        string expected
    ) => Assert.Equal(expected, new AgreementPair { Pair = pair, Status = "relative" }.Detectors);

    [Fact]
    public void The_pairs_of_a_current_run_never_show_an_internal_name()
    {
        var shown = new[] { "madmom+ffmpeg", "edma+ffmpeg", "madmom+edma" }.Select(pair =>
            new AgreementPair { Pair = pair, Status = "full" }.Detectors
        );

        Assert.All(shown, text => Assert.DoesNotMatch("ffmpeg|edma|[+_]", text));
    }
}
