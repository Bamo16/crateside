using System.Diagnostics;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.WorkLists;

/// <summary>A measurement of what a Queue refresh costs, not an assertion about correctness.</summary>
public class ProcessedTracksCostTests
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string MashupRoot = @"P:\RYAN\Ableton\Mashup Samples";

    [Fact]
    public void The_disk_scan_is_the_expensive_half_of_a_refresh()
    {
        Assert.SkipUnless(
            File.Exists(LedgerPath) && Directory.Exists(MashupRoot),
            "Needs the real ledger and stem roots."
        );

        var ledger = KeyLedger.Read(LedgerPath);

        var stopwatch = Stopwatch.StartNew();
        var processed = ProcessedTracks.Scan(ledger, [StagingRoot, MashupRoot]);
        stopwatch.Stop();

        var files =
            Directory.GetFiles(MashupRoot, "*", SearchOption.AllDirectories).Length
            + Directory.GetFiles(StagingRoot, "*", SearchOption.AllDirectories).Length;

        Assert.NotEmpty(processed.FullyProcessed);

        // Deliberately loose: real-library I/O, where the order of magnitude is the point.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 60_000,
            $"scanned {files} files in {stopwatch.ElapsedMilliseconds} ms"
        );
    }
}
