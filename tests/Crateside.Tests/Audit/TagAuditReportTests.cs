using Crateside.Core.Audit;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Audit;

/// <summary>
/// The readings the audit takes of a stem list, tested without a disk. What a root's silence
/// means is the whole subject: see <see cref="AuditRoot"/> and ADR 0003.
/// </summary>
public class TagAuditReportTests
{
    [Fact]
    public void An_absent_key_is_untagged_where_crateside_writes_the_tag_store()
    {
        var report = new TagAuditReport([Stem(applied: null, tagStoreIsWritten: true)]);

        Assert.Single(report.Untagged);
    }

    [Fact]
    public void An_absent_key_is_no_finding_where_the_tag_store_is_only_mirrored()
    {
        var report = new TagAuditReport([Stem(applied: null, tagStoreIsWritten: false)]);

        Assert.Empty(report.Untagged);
        Assert.True(report.IsClean);
    }

    [Fact]
    public void A_disagreement_stands_in_a_mirrored_root_because_the_key_is_there_to_read()
    {
        var report = new TagAuditReport([Stem(applied: "C major", tagStoreIsWritten: false)]);

        Assert.Single(report.Disagreements);
    }

    [Fact]
    public void A_partial_key_stands_in_a_mirrored_root_for_the_same_reason()
    {
        var report = new TagAuditReport([
            Stem(applied: null, tagStoreIsWritten: false) with
            {
                HasPartialKey = true,
            },
        ]);

        Assert.Single(report.PartialKeys);
    }

    [Fact]
    public void Siblings_carrying_different_keys_are_inconsistent_wherever_they_sit()
    {
        var report = new TagAuditReport([
            Stem(applied: "C major", tagStoreIsWritten: false),
            Stem(applied: "D minor", tagStoreIsWritten: false) with
            {
                FileName = "song (Vocal - Full).flac",
            },
        ]);

        Assert.Single(report.Inconsistencies);
    }

    [Fact]
    public void A_withheld_verdict_leaves_an_untagged_stem_alone()
    {
        var report = new TagAuditReport([
            Stem(applied: null, tagStoreIsWritten: true) with
            {
                LedgerKey = null,
                HasTaggableVerdict = false,
            },
        ]);

        Assert.Empty(report.Untagged);
    }

    private static TaggedStem Stem(string? applied, bool tagStoreIsWritten) =>
        new()
        {
            Path = @"C:\root\song (Instrumental - Full).flac",
            FileName = "song (Instrumental - Full).flac",
            Root = @"C:\root",
            TagStoreIsWritten = tagStoreIsWritten,
            SourceVideoId = "abc123",
            Song = "artist - song",
            LedgerKey = "A minor",
            HasTaggableVerdict = true,
            Confidence = KeyConfidence.High,
            AppliedKey = applied,
        };
}
