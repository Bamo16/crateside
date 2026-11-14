using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Crateside.Core.Pairing;

/// <summary>Ableton's Re-Pitch warp mode, where speed and pitch move together (#58).</summary>
public static class RePitch
{
    public const int DefaultMaxShift = 3;
    public const int MaxShift = 12;

    private const double FastestBpm = 999;

    /// <summary>The arrangement BPM that plays a track <paramref name="semitones"/> higher or lower.</summary>
    public static double ArrangementBpm(double trackBpm, int semitones) =>
        trackBpm * Math.Pow(2, semitones / 12.0);

    /// <summary>A typed BPM, with a comma or a point for the decimal; false for none, zero, or past 999.</summary>
    public static bool TryParseBpm(string? text, [NotNullWhen(true)] out double? bpm)
    {
        bpm =
            double.TryParse(
                text?.Trim().Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed
            ) && parsed is > 0 and <= FastestBpm
                ? parsed
                : null;

        return bpm is not null;
    }
}
