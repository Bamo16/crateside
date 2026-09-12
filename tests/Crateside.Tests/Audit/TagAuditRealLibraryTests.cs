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
    public void The_audit_never_calls_a_file_untagged_that_carries_a_key_anywhere()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // This replaces an assertion that both roots audit clean. They did on 2026-09-10 and do
        // not now: 110 files crateside had tagged have since lost their Ableton keywords (#24).
        // Those findings are real -- confirmed in the browser -- so the test was right and the
        // library was wrong, which is not something a test can hold still.
        //
        // What is worth holding is the fear ADR 0011 actually had: that reading a lagging store
        // would make the audit invent findings. A file reported untagged must carry no key in
        // *either* store, whichever one its root is audited from.
        var xmp = new AbletonFolderInfoReader();
        var index = new AbletonIndexReader(Options.Create(Paths));

        foreach (var stem in Report().Untagged)
        {
            var fromXmp = xmp.Read(stem.Root).TryGet(stem.Path, out var x) && x.IsKeyTagged;
            var fromIndex = index.Read(stem.Root).TryGet(stem.Path, out var i) && i.IsKeyTagged;

            Assert.False(
                fromXmp || fromIndex,
                $"{stem.FileName} is reported untagged but carries a key "
                    + $"(xmp: {fromXmp}, index: {fromIndex})."
            );
        }
    }

    [Fact]
    public void The_index_never_knows_less_than_the_xmp()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // Why the audit reads the index for a root crateside does not write. This used to assert
        // the stronger thing -- that the XMP lags far enough to invent findings -- which was true
        // while it was 108 files behind and stopped being true the moment that gap was repaired
        // (#24). The containment is the part that does not depend on the day.
        //
        // The reverse direction is the one that would matter: a file keyed in the XMP and not the
        // index would mean the index is not the tag state and ADR 0013 is wrong.
        var index = new AbletonIndexReader(Options.Create(Paths)).Read(LibraryRoot);
        var xmp = new AbletonFolderInfoReader().Read(LibraryRoot);

        var xmpOnly = xmp
            .Entries.Where(e => e.IsKeyTagged)
            .Where(e => !(index.TryGet(e.RelativePath, out var i) && i.IsKeyTagged))
            .Select(e => e.RelativePath)
            .ToArray();

        Assert.True(
            xmpOnly.Length is 0,
            $"{xmpOnly.Length} file(s) are keyed in the XMP and not in the index, so the index is "
                + $"not the tag state: {string.Join(", ", xmpOnly.Take(5))}"
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
