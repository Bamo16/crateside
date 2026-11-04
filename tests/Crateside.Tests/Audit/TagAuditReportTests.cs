using Crateside.Core.Audit;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Audit;

/// <summary>The readings the audit takes of a stem list, tested without a disk.</summary>
public sealed class TagAuditReportTests
{
    [Fact]
    public void An_absent_key_against_a_taggable_verdict_is_untagged()
    {
        var report = new TagAuditReport([Stem(applied: null)]);

        Assert.Single(report.Untagged);
    }

    [Fact]
    public void An_absent_key_with_nothing_to_tag_it_with_is_not_a_finding()
    {
        var report = new TagAuditReport([
            Stem(applied: null) with
            {
                LedgerKey = null,
                HasTaggableVerdict = false,
            },
        ]);

        Assert.Empty(report.Untagged);
        Assert.True(report.IsClean);
    }

    [Fact]
    public void An_applied_key_that_is_not_the_verdict_is_a_disagreement()
    {
        var report = new TagAuditReport([Stem(applied: "C major")]);

        Assert.Single(report.Disagreements);
    }

    [Fact]
    public void A_partial_key_is_a_finding_on_its_own()
    {
        var report = new TagAuditReport([Stem(applied: null) with { HasPartialKey = true }]);

        Assert.Single(report.PartialKeys);
    }

    [Fact]
    public void Siblings_carrying_different_keys_are_inconsistent_wherever_they_sit()
    {
        var report = new TagAuditReport([
            Stem(applied: "C major"),
            Stem(applied: "D minor") with
            {
                FileName = "song (Vocal - Full).flac",
            },
        ]);

        Assert.Single(report.Inconsistencies);
    }

    [Fact]
    public void A_root_crateside_writes_is_read_from_the_xmp_and_every_other_from_the_index()
    {
        Assert.False(AuditRoot.Written(@"C:\staging").ReadsTheIndex);
        Assert.True(AuditRoot.Mirrored(@"C:\library").ReadsTheIndex);
    }

    private static TaggedStem Stem(string? applied) =>
        new()
        {
            Path = @"C:\root\song (Instrumental - Full).flac",
            FileName = "song (Instrumental - Full).flac",
            Root = @"C:\root",
            SourceTrackKey = "abc123",
            Song = "artist - song",
            LedgerKey = "A minor",
            HasTaggableVerdict = true,
            Confidence = KeyConfidence.High,
            AppliedKey = applied,
        };
}
