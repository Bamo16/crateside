using Crateside.Core;
using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Audit;

/// <summary>The audit run against the user's actual library.</summary>
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

        Assert.True(
            report.ScannedCount > Directory.GetFiles(StagingRoot, "*.flac").Length,
            "The audit saw no more stems than staging holds, so it never reached the library."
        );
    }

    [Fact]
    public void The_audit_never_calls_a_file_untagged_that_carries_a_key_anywhere()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // A file reported untagged must carry no key in either store (ADR 0011).
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

        // A file keyed in the XMP and not the index would mean ADR 0013 is wrong.
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

        Assert.All(Report().Untagged, stem => Assert.NotNull(stem.LedgerKey));
    }

    [Fact]
    public void A_stem_the_ledger_has_never_seen_is_never_an_untagged_finding()
    {
        Assert.SkipUnless(Available, "Both stem roots and the ledger must be present.");

        // Untagged needs a taggable verdict, and no verdict is not one (ADR 0017).
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

        // If this stops holding, revisit the sibling check rather than fixing the test.
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
