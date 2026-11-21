using Crateside.Core.KeyDetection;

namespace Crateside.Tests.KeyDetection;

/// <summary>keydetect-harness report 15's ledger cases, on keydetect 1.3.0's chroma averaged into 0.5 s units.</summary>
public sealed class KeyLiftsTests
{
    [Fact]
    public void TAPI_TAPI_lifts_a_semitone_where_its_F_section_starts()
    {
        var lifts = KeyLifts.Find(Chroma("keydetect-chroma-tapi-tapi.txt"));

        Assert.Equal([116.0, 124.0], lifts.Select(lift => lift.At));
        Assert.All(lifts, lift => Assert.Equal(1, lift.Shift));
        Assert.All(lifts, lift => Assert.Equal(0.2785, lift.Fit, 3));
        Assert.Equal(0.949, lifts[1].Strength, 3);
    }

    [Fact]
    public void Persona_has_no_lift_though_the_matcher_finds_copies_there()
    {
        Assert.Empty(KeyLifts.Find(Chroma("keydetect-chroma-persona.txt")));
    }

    [Fact]
    public void Chroma_lines_are_read_in_time_order_and_other_lines_are_skipped()
    {
        var windows = KeyLifts.Parse(
            """
            [Parsed_ametadata_2 @ 0000] lavfi.keydetect.chroma1=0.100:0.5,0,0,0,0,0,0,0,0,0,0,1
            [Parsed_ametadata_1 @ 0000] lavfi.keydetect.chroma0=0.050:1,0,0,0,0,0,0,0,0,0,0,0.25
            [Parsed_ametadata_1 @ 0000] lavfi.keydetect.bass_chroma0=0.050:1,0,0,0,0,0,0,0,0,0,0,0
            [Parsed_ametadata_1 @ 0000] lavfi.keydetect.chroma0=0.150:1,0,0
            """
        );

        Assert.Equal([0.05, 0.1], windows.Select(window => window.Time));
        Assert.Equal(0.25, windows[0].Chroma[11]);
    }

    [Fact]
    public void No_chroma_finds_no_lift()
    {
        Assert.Empty(KeyLifts.Find([]));
    }

    private static List<ChromaWindow> Chroma(string fixture) =>
        KeyLifts.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture))
        );
}
