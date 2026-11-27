using System.Diagnostics;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.WorkLists;

/// <summary>A measurement of what a Queue refresh costs, not an assertion about correctness.</summary>
public sealed class ProcessedTracksCostTests
{
    [Fact]
    public void The_disk_scan_is_the_expensive_half_of_a_refresh()
    {
        var (staging, library) = (RealPaths.Staging(), RealPaths.Library());
        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");

        var ledger = TestLedger.FromReal();

        var stopwatch = Stopwatch.StartNew();
        var processed = ProcessedTracks.Scan(ledger, [staging, library]);
        stopwatch.Stop();

        var files =
            Directory.GetFiles(library, "*", SearchOption.AllDirectories).Length
            + Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Length;

        Assert.SkipWhen(processed.FullyProcessed.Count is 0, "No track here is fully processed.");

        // Deliberately loose: real-library I/O, where the order of magnitude is the point.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 60_000,
            $"scanned {files} files in {stopwatch.ElapsedMilliseconds} ms"
        );
    }
}
