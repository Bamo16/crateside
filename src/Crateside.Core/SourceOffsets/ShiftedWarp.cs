using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Core.SourceOffsets;

/// <summary>An added source's sidecar derived from the mix's warp, and what the shift did to its clip start.</summary>
/// <param name="ClipStartWasBar">The bar the mix's clip starts on, when the added source starts too late for it.</param>
/// <param name="ClipStartNowBar">The first bar line the added source's audio holds.</param>
public sealed record ShiftedFile(AnalysisFile File, int? ClipStartWasBar, int? ClipStartNowBar)
{
    public bool ClipStartMoved => ClipStartWasBar is not null;
}

/// <summary>The mix's warp shifted onto an added source by its measured offset (ADR 0111, rules 15 to 18).</summary>
public static class ShiftedWarp
{
    private const double HiddenMarkerBeats = 1.0 / 32;

    /// <summary>Null when the mix's warp leaves nothing of the added source to warp.</summary>
    public static ShiftedFile? Derive(
        AnalysisFile mix,
        AnalysisFile source,
        int offsetSamples,
        int sampleRate,
        double sourceSeconds
    )
    {
        if (
            mix.Warp.Markers is not { Count: > 1 } markers
            || mix.DefaultClip is not { Start: var mixStart }
        )
            return null;

        var shift = (double)offsetSamples / sampleRate;
        var shifted = Markers(mix.Warp, markers, shift, sourceSeconds);

        if (shifted is null)
            return null;

        var warp = mix.Warp with { Markers = shifted };
        var ruler = BarRuler.Of(mix.Warp.TimeSignature);
        var startSeconds = mix.Warp.SecondsAt(mixStart) - shift;
        var (start, wasBar, nowBar) = startSeconds is >= 0
            ? (mixStart, (int?)null, (int?)null)
            : MovedStart(mix.Warp, ruler, shift, mixStart);
        var end = WarpEdit.BeatAt(warp, sourceSeconds) ?? start;

        return new ShiftedFile(
            source with
            {
                Warp = warp,
                IsDefaultClipSaved = true,
                Clip = source.Clip with
                {
                    LoopStart = start,
                    HiddenLoopStart = start,
                    SampleOffset = 0,
                    LoopEnd = end,
                    HiddenLoopEnd = end,
                    OutMarker = end,
                    LoopOn = false,
                },
            },
            wasBar,
            nowBar
        );
    }

    private static (double Start, int? WasBar, int? NowBar) MovedStart(
        Warp mix,
        BarRuler ruler,
        double shift,
        double mixStart
    )
    {
        var firstBeat = WarpEdit.BeatAt(mix, shift) ?? mixStart;
        var index = (int)Math.Ceiling((firstBeat / ruler.BeatsPerBar) - 1e-6);

        return (index * ruler.BeatsPerBar, ruler.BarAt(mixStart), BarRuler.Number(index));
    }

    private static List<WarpMarker>? Markers(
        Warp mix,
        IReadOnlyList<WarpMarker> markers,
        double shift,
        double sourceSeconds
    )
    {
        List<WarpMarker> kept =
        [
            .. markers
                .Select(marker => new WarpMarker(marker.Seconds - shift, marker.Beat))
                .Where(marker => marker.Seconds >= 0 && marker.Seconds <= sourceSeconds),
        ];
        var droppedFront = markers.Any(marker => marker.Seconds - shift < 0);
        var droppedBack = markers.Any(marker => marker.Seconds - shift > sourceSeconds);

        if (droppedFront && WarpEdit.BeatAt(mix, shift) is { } beat)
            kept.Insert(0, new WarpMarker(0, beat));

        if ((droppedBack || kept.Count is 1) && kept is [.., var last])
            kept.Add(Hidden(mix, last, shift));

        return kept.Count > 1 ? kept : null;
    }

    private static WarpMarker Hidden(Warp mix, WarpMarker after, double shift)
    {
        var beatsPerSecond = (mix.TempoAt(after.Seconds + shift) ?? 120) / 60;

        return new WarpMarker(
            after.Seconds + (HiddenMarkerBeats / beatsPerSecond),
            after.Beat + HiddenMarkerBeats
        );
    }
}
