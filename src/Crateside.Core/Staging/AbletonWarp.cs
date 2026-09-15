namespace Crateside.Core.Staging;

/// <summary>What the `.asd` sidecar records about how Ableton warps a sample.</summary>
public sealed record AbletonWarp(
    bool IsWarped,
    WarpMode Mode,
    int TimeSignatureNumerator,
    int TimeSignatureDenominator,
    IReadOnlyList<WarpMarker> Markers
)
{
    /// <summary>The tempo of the segment <paramref name="seconds"/> falls in.</summary>
    public double? TempoAt(double seconds)
    {
        if (Markers is not { Count: > 1 })
            return null;

        for (var i = Markers.Count - 2; i > 0; i--)
            if (seconds >= Markers[i].Seconds)
                return Tempo(Markers[i], Markers[i + 1]);

        return Tempo(Markers[0], Markers[1]);
    }

    private static double? Tempo(WarpMarker from, WarpMarker to)
    {
        var span = to.Seconds - from.Seconds;

        return span > 0 ? (to.Beat - from.Beat) / span * 60 : null;
    }
}

/// <param name="Seconds">Position in the audio file.</param>
/// <param name="Beat">Position on Ableton's grid.</param>
/// <remarks>
/// A tempo is the slope between two consecutive markers; the sidecar stores no tempo of its own,
/// so a single marker carries none.
/// </remarks>
public readonly record struct WarpMarker(double Seconds, double Beat);

public enum WarpMode
{
    Beats,
    Tones,
    Texture,
    RePitch,
    Complex,
    Rex,
    ComplexPro,
}
