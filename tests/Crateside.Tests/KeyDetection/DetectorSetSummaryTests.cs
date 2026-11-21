using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;

namespace Crateside.Tests.KeyDetection;

public sealed class DetectorSetSummaryTests
{
    [Fact]
    public void The_summary_names_what_each_detector_read()
    {
        var summary = DetectorSet.Summarize(
            new AlgorithmsDto
            {
                FfmpegKeydetect = new() { Key = "D major", R = 0.784 },
                Madmom = new() { Key = "D major" },
                EssentiaEdma = new() { Key = "B minor" },
            }
        );

        Assert.Equal("keydetect D major (r 0.78), madmom D major, edma B minor", summary);
    }

    [Fact]
    public void A_detector_that_read_no_key_says_so()
    {
        var summary = DetectorSet.Summarize(
            new AlgorithmsDto
            {
                FfmpegKeydetect = new() { Error = "no chords" },
                Madmom = new() { Key = "A minor" },
            }
        );

        Assert.Equal("keydetect no key, madmom A minor, edma no key", summary);
    }
}
