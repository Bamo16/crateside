using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;

namespace Crateside.Tests.Orchestration.Tagging;

/// <summary>Which <c>Type</c> a file is tagged with: the part it plays in a mashup.</summary>
public class TaggingRunnerTypeTests
{
    private static string? TypeKeyword(string? preset, SampleType? type) =>
        TaggingRunner
            .KeywordsFor("G# minor", preset, type)
            .SingleOrDefault(keyword => keyword.StartsWith("Type|", StringComparison.Ordinal));

    [Fact]
    public void A_stem_takes_its_type_from_its_preset()
    {
        Assert.Equal("Type|Vocal", TypeKeyword("Vocal - Full", type: null));
        Assert.Equal("Type|Instrumental", TypeKeyword("Instrumental - Clean", type: null));
    }

    [Fact]
    public void A_file_never_cut_takes_the_type_the_user_chose()
    {
        Assert.Equal("Type|Vocal", TypeKeyword(preset: null, SampleType.Vocal));
        Assert.Equal("Type|Instrumental", TypeKeyword(preset: null, SampleType.Instrumental));
    }

    [Fact]
    public void A_kept_source_is_an_instrumental()
    {
        // A kept full mix is used as an instrumental, and belongs under that filter (ADR 0028).
        Assert.Equal("Type|Instrumental", TypeKeyword(preset: null, type: null));
    }
}
