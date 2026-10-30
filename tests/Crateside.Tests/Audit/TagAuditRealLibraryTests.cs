using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Audit;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Audit;

/// <summary>The audit run against the user's actual library.</summary>
public sealed class TagAuditRealLibraryTests
{
    private static readonly CratesidePaths Paths = RealPaths.Create();

    private static (string Staging, string Library) Roots()
    {
        var roots = (RealPaths.Staging(), RealPaths.Library());

        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");
        Assert.SkipUnless(Directory.Exists(Paths.AbletonIndex), "Live's index is not here.");

        return roots;
    }

    private static TagAuditReport Report()
    {
        var (staging, library) = Roots();

        return TagAudit.Run(
            TestLedger.FromReal(),
            [AuditRoot.Written(staging), AuditRoot.Mirrored(library)],
            TagAudit.Using(new FileIndexReader(Paths.AbletonIndex))
        );
    }

    [Fact]
    public void The_audit_reaches_stems_that_have_left_staging()
    {
        var (staging, library) = Roots();
        Assert.SkipUnless(RealLibrary.HasStems(library), "The library holds no stems.");

        var report = Report();

        Assert.True(
            report.ScannedCount > Directory.GetFiles(staging, "*.flac").Length,
            "The audit saw no more stems than staging holds, so it never reached the library."
        );
    }

    [Fact]
    public void The_audit_never_calls_a_file_untagged_that_carries_a_key_anywhere()
    {
        // A file reported untagged must carry no key in either store (ADR 0011).
        var xmp = new FolderInfoReader();
        var index = new FileIndexReader(Paths.AbletonIndex);

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
        // A file keyed in the XMP and not the index would mean ADR 0013 is wrong.
        var (_, library) = Roots();
        var index = new FileIndexReader(Paths.AbletonIndex).Read(library);
        var xmp = new FolderInfoReader().Read(library);

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
    public void A_withheld_verdict_is_not_reported_as_an_untagged_stem() =>
        Assert.All(Report().Untagged, stem => Assert.NotNull(stem.LedgerKey));

    [Fact]
    public void A_stem_the_ledger_has_never_seen_is_never_an_untagged_finding()
    {
        // Untagged needs a taggable verdict, and no verdict is not one (ADR 0017).
        var report = Report();

        Assert.SkipWhen(
            report.NoVerdictCount is 0,
            "Every stem has a verdict, so neither this nor the ledger-free sibling check covers anything."
        );
        Assert.All(report.Untagged, stem => Assert.NotNull(stem.Song));
    }

    [Fact]
    public void An_inconsistent_song_names_the_keys_that_are_in_conflict()
    {
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
    public void Every_finding_points_at_a_file_that_exists()
    {
        var report = Report();

        Assert.All(
            [
                .. report.Disagreements,
                .. report.Untagged,
                .. report.PartialKeys,
                .. report.Inconsistencies.SelectMany(s => s.Stems),
            ],
            stem => Assert.True(File.Exists(stem.Path), stem.Path)
        );
    }
}
