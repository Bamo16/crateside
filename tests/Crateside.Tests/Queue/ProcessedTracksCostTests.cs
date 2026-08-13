using System.Diagnostics;
using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// Not an assertion about correctness — a measurement of what a Queue refresh actually costs,
/// so the answer to "are we doing more work than we need to?" is a number rather than a guess.
/// </summary>
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

        // Recorded so a regression is visible; deliberately loose, since it is I/O on a real
        // library and the point is the order of magnitude, not a threshold.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 60_000,
            $"scanned {files} files in {stopwatch.ElapsedMilliseconds} ms"
        );
    }
}
