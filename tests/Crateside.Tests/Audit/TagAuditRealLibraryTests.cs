using Crateside.Core;
using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Audit;

/// <summary>
/// The audit run against the user's actual library. Asserted here rather than only in the app
/// because the whole value of the check is whether it holds against real, hand-applied tags —
/// fixtures cannot disagree with a ledger in the ways a person can.
/// </summary>
public class TagAuditRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string LibraryRoot = @"P:\RYAN\Ableton\Mashup Samples";
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private static bool Available =>
        Directory.Exists(LibraryRoot) && File.Exists(LedgerPath) && Directory.Exists(StagingRoot);

    private static CratesidePaths Paths =>
        new()
        {
            SampleStaging = StagingRoot,
            MashupSamples = LibraryRoot,
            Ledger = LedgerPath,
        };

    /// <summary>Each root read from the store that holds its tags — the shipped arrangement.</summary>
    private static TagAuditReport Report() =>
        TagAudit.Run(
            KeyLedger.Read(LedgerPath),
            [AuditRoot.Written(StagingRoot), AuditRoot.Mirrored(LibraryRoot)],
            TagAudit.Using(new AbletonIndexReader(Options.Create(Paths)))
        );

    [Fact]
    public void The_audit_reaches_stems_that_have_left_staging()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        var report = Report();

        // The point of the audit: the finished library is in scope, not just the board.
        Assert.True(
            report.ScannedCount > Directory.GetFiles(StagingRoot, "*.flac").Length,
            "The audit saw no more stems than staging holds, so it never reached the library."
        );
    }

    [Fact]
    public void Both_roots_audit_clean_which_is_now_an_answer_rather_than_a_refusal()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // Measured 2026-09-10: zero findings across 548 stems, with the library in scope for
        // every reading rather than excluded from Untagged by ADR 0011's rule.
        //
        // The audit has no screen of its own, so this assertion is its surface. A failure here
        // is a real finding in the library to go and look at -- not a broken test to relax.
        var report = Report();

        Assert.True(
            report.IsClean,
            $"{report.Untagged.Count} untagged, {report.Disagreements.Count} disagreeing, "
                + $"{report.PartialKeys.Count} partial, {report.Inconsistencies.Count} inconsistent."
        );
    }

    [Fact]
    public void Reading_the_library_from_the_xmp_would_invent_findings_the_index_does_not()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // The measurement this whole change turns on. The XMP is ~108 files behind and was
        // still behind after Ableton rewrote it, so auditing the library through it would
        // report scores of tagged files as untagged -- ADR 0011's "findings the user would
        // learn to scroll past".
        var viaXmp = TagAudit
            .Run(
                KeyLedger.Read(LedgerPath),
                [AuditRoot.Mirrored(LibraryRoot)],
                root => new AbletonFolderInfoReader().Read(root.Path)
            )
            .Untagged.Count;

        var viaIndex = Report()
            .Untagged.Count(stem =>
                stem.Root.Equals(LibraryRoot, StringComparison.OrdinalIgnoreCase)
            );

        Assert.True(
            viaXmp > viaIndex,
            $"The XMP reported {viaXmp} untagged and the index {viaIndex}. If these ever agree, "
                + "the XMP has caught up and ADR 0017's reason for reading the index is gone."
        );
    }

    [Fact]
    public void A_ledger_disagreement_always_carries_both_sides_so_the_user_can_judge()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // crateside cannot tell a mis-tag from a supersede, so it must never report one
        // without showing what each side says.
        Assert.All(
            Report().Disagreements,
            stem =>
            {
                Assert.NotNull(stem.AppliedKey);
                Assert.NotNull(stem.LedgerKey);
                Assert.NotEqual(stem.AppliedKey, stem.LedgerKey);
                Assert.NotNull(stem.Song);
            }
        );
    }

    [Fact]
    public void A_withheld_verdict_is_not_reported_as_an_untagged_stem()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // "No single safe tag" means the stem is *supposed* to be untagged. Reporting it
        // would train the user to ignore the list.
        Assert.All(Report().Untagged, stem => Assert.NotNull(stem.LedgerKey));
    }

    [Fact]
    public void A_stem_the_ledger_has_never_seen_is_never_an_untagged_finding()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // Measured 2026-09-10: the three library files the index does not key are exactly
        // these, and all three are unjudged. ADR 0013 called them "the honest finding count",
        // which conflated files-the-index-does-not-key with findings -- Untagged needs a
        // taggable verdict, and no verdict is not one (ADR 0017).
        var report = Report();

        Assert.True(report.NoVerdictCount > 0, "Nothing is unjudged, so this covers nothing.");
        Assert.All(report.Untagged, stem => Assert.NotNull(stem.Song));
    }

    [Fact]
    public void An_inconsistent_song_names_the_keys_that_are_in_conflict()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        Assert.All(
            Report().Inconsistencies,
            song =>
            {
                // The finding *is* the disagreement, so there must be at least two keys and
                // at least two stems to have disagreed.
                Assert.True(song.AppliedKeys.Count > 1);
                Assert.True(song.Stems.Count > 1);
                Assert.NotEmpty(song.Label);
            }
        );
    }

    [Fact]
    public void The_sibling_check_reaches_stems_the_ledger_has_never_seen()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        var report = Report();

        // Roughly a third of this library predates the ledger. If that stops being true the
        // sibling check loses its reason to exist, and this test should be revisited rather
        // than fixed.
        Assert.True(
            report.NoVerdictCount > 0,
            "Every stem now has a verdict, so the ledger-free check covers nothing extra."
        );
    }

    [Fact]
    public void Every_finding_points_at_a_file_that_exists()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        var report = Report();

        Assert.All(
            [
                .. report.Disagreements,
                .. report.Untagged,
                .. report.PartialKeys,
                .. report.Inconsistencies.SelectMany(s => s.Stems),
            ],
            (TaggedStem stem) => Assert.True(File.Exists(stem.Path), stem.Path)
        );
    }
}
