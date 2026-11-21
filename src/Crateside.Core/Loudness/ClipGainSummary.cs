using System.Globalization;
using Crateside.Core.Samples;

namespace Crateside.Core.Loudness;

/// <summary>The one line the activity feed shows for a sample whose clip gain was set (#170).</summary>
public static class ClipGainSummary
{
    public static string Of(string audioPath, float gain, double targetLufs)
    {
        var what = ClipGains.IsOnTarget(gain, 1)
            ? "clip gain left at 0 dB"
            : $"clip gain {Signed(20 * Math.Log10(gain), "+0.0")} dB to {Signed(targetLufs, "0.#")} LUFS";

        return StemFileName.PresetOf(audioPath) is { } preset ? $"{preset}: {what}" : what;
    }

    private static string Signed(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('-', '−');
}
