using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration;

public sealed class SourceBackfillRealLibraryTests
{
    [Fact]
    public void Every_missing_source_is_a_library_track_that_holds_none()
    {
        var paths = new CratesidePaths();
        Assert.SkipUnless(
            TestLedger.HasReal && Directory.Exists(paths.LibraryFolder),
            "no real ledger or library on this machine"
        );

        var set = new WorkingSet();
        using var store = TestLedger.StoreReal();
        var missing = new SourceBackfill(
            store,
            set,
            new DownloadRunner(new ProcessRunner()),
            TimeProvider.System,
            paths
        ).Missing();
        var processed = ProcessedTracks.Scan(
            TestLedger.FromReal(),
            [paths.StagingFolder, paths.LibraryFolder]
        );

        Assert.All(
            missing,
            videoId =>
            {
                Assert.False(set.TryGetSource(videoId, out _));
                Assert.DoesNotContain(processed.StemsFor(videoId), stem => stem.IsVerbatimSource);
                Assert.Contains(
                    processed.StemsFor(videoId),
                    stem =>
                        stem.Root.Equals(paths.LibraryFolder, StringComparison.OrdinalIgnoreCase)
                );
            }
        );
    }
}
