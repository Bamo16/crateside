using Crateside.Core.Audit;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Tagging;

namespace Crateside.Tests.Orchestration.Tagging;

public sealed class TaggingSummaryTests
{
    private static TaggedStem Stem(string? ledgerKey) =>
        new()
        {
            Path = "S:/Staging/Song (Vocal - Full).flac",
            FileName = "Song (Vocal - Full).flac",
            Root = "S:/Staging",
            SourceTrackKey = "dQw4w9WgXcQ",
            LedgerKey = ledgerKey,
        };

    [Fact]
    public void A_pass_names_the_key_it_wrote_and_how_many_files_took_it()
    {
        Assert.Equal(
            "tagged 2 files D major",
            new TaggingFacts(2, TaggingDrainer.KeyOf([Stem("D major"), Stem("D major")])).Text
        );
    }

    [Fact]
    public void A_pass_over_files_with_no_one_key_names_none()
    {
        Assert.Equal(
            "tagged 1 file",
            new TaggingFacts(1, TaggingDrainer.KeyOf([Stem("D major"), Stem(null)])).Text
        );
    }
}
