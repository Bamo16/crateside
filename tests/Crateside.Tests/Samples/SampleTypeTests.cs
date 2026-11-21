using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

public sealed class SampleTypeTests
{
    [Theory]
    [InlineData("Kreayshawn - Gucci Gucci (Acapella)")]
    [InlineData("Lil Wayne - A Milli (A Cappella)")]
    [InlineData("Fatman Scoop - Be Faithful Acappella")]
    [InlineData("Ciara - How Low (Vocal Isolation)")]
    [InlineData("Daft Punk - Harder Better Faster Stronger (Vocals Only)")]
    public void A_title_that_says_vocal_reads_as_vocal(string title)
    {
        Assert.Equal(SampleType.Vocal, SampleTypes.InferredFrom(title));
    }

    [Theory]
    [InlineData("Ludacris - Stand Up (Instrumental)")]
    [InlineData("TLC - No Scrubs (Official Instrumental)")]
    [InlineData("Snail's House - Pixel Galaxy (Inst.)")]
    [InlineData("Queen - Bohemian Rhapsody (Karaoke Version)")]
    [InlineData("Adele - Hello (No Vocals)")]
    [InlineData("YOASOBI - Idol (Off Vocal)")]
    public void A_title_that_says_instrumental_reads_as_instrumental(string title)
    {
        Assert.Equal(SampleType.Instrumental, SampleTypes.InferredFrom(title));
    }

    [Theory]
    [InlineData("Kreayshawn - Gucci Gucci (Clean)")]
    [InlineData("Warren G - Regulate (Radio Edit)")]
    [InlineData("Acapella and Instrumental - both in one upload")]
    [InlineData("")]
    [InlineData(null)]
    public void A_title_that_is_unclear_or_contradicts_itself_reads_as_nothing(string? title)
    {
        Assert.Null(SampleTypes.InferredFrom(title));
    }

    [Fact]
    public void Each_type_has_the_label_the_user_already_uses()
    {
        Assert.Equal("A Cappella", SampleTypes.LabelFor(SampleType.Vocal));
        Assert.Equal("Instrumental", SampleTypes.LabelFor(SampleType.Instrumental));
    }
}
