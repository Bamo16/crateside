using Crateside.Core;
using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

/// <summary>The reader against every <c>.asd</c> the real library holds.</summary>
public class AbletonWarpReaderRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();

    [Fact]
    public void Every_sidecar_in_the_library_parses()
    {
        var unreadable = Sidecars()
            .Where(file => !AbletonWarpReader.TryRead(file, out _))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(unreadable);
    }

    [Fact]
    public void A_saved_default_clip_and_warp_markers_come_together()
    {
        var disagreeing = Sidecars()
            .Select(file => (File: Path.GetFileName(file), Warp: Read(file)))
            .Where(s => s.Warp.DefaultClip is null == s.Warp.Markers.Count > 0)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(disagreeing);
    }

    [Fact]
    public void A_saved_default_clip_ends_after_it_starts()
    {
        var backwards = Sidecars()
            .Select(file => (File: Path.GetFileName(file), Warp: Read(file)))
            .Where(s => s is { Warp.DefaultClip: { } clip } && clip.End <= clip.Start)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(backwards);
    }

    [Fact]
    public void Every_sidecar_in_the_library_reaches_its_overview()
    {
        var without = Sidecars()
            .Where(file => Read(file).Overview is not { Peaks.Count: > 0 })
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(without);
    }

    [Fact]
    public void Markers_advance_in_both_seconds_and_beats()
    {
        var outOfOrder = Sidecars()
            .Select(file => (File: Path.GetFileName(file), Warp: Read(file)))
            .Where(s =>
                s.Warp.Markers.Zip(s.Warp.Markers.Skip(1)).Any(p => !Advances(p.First, p.Second))
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(outOfOrder);
    }

    [Fact]
    public void A_sample_with_markers_has_a_positive_tempo_at_each_of_them()
    {
        var nonsense = Sidecars()
            .Select(file => (File: Path.GetFileName(file), Warp: Read(file)))
            .Where(s => s.Warp.Markers.Count > 1)
            .Where(s => s.Warp.Markers.Any(m => s.Warp.TempoAt(m.Seconds) is not > 0))
            .Select(s => s.File)
            .ToList();

        Assert.Empty(nonsense);
    }

    [Fact]
    public void The_time_signature_reads_as_a_usable_meter()
    {
        var unusable = Sidecars()
            .Select(Read)
            .Where(w =>
                w.TimeSignatureNumerator is < 1 or > 32 || w.TimeSignatureDenominator is < 1
            )
            .ToList();

        Assert.Empty(unusable);
    }

    private static bool Advances(WarpMarker first, WarpMarker second) =>
        second.Seconds >= first.Seconds && second.Beat > first.Beat;

    private static AbletonWarp Read(string file)
    {
        Assert.True(AbletonWarpReader.TryRead(file, out var warp), file);

        return warp;
    }

    private static List<string> Sidecars()
    {
        SkipUnlessPresent();

        return
        [
            .. Directory.EnumerateFiles(Paths.MashupSamples, "*.asd", SearchOption.AllDirectories),
        ];
    }

    private static void SkipUnlessPresent() =>
        Assert.SkipUnless(
            Directory.Exists(Paths.MashupSamples),
            $"{Paths.MashupSamples} not present on this machine."
        );
}
