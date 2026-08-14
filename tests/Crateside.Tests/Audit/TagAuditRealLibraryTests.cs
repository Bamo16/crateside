using Crateside.Core.Audit;
using Crateside.Core.Ledger;

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

    private static TagAuditReport Report() =>
        TagAudit.Run(KeyLedger.Read(LedgerPath), [StagingRoot, LibraryRoot]);

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

        // Roughly half this library predates the ledger. If that stops being true the sibling
        // check loses its reason to exist, and this test should be revisited rather than fixed.
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
