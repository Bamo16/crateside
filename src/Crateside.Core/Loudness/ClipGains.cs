using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.Loudness;

/// <summary>The clip gain, linear, that brings a sample's whole file to the loudness target (ADR 0059).</summary>
public sealed class ClipGains(LoudnessReader reader, Func<LoudnessOptions> options)
{
    /// <summary>A gain this close to the one wanted, in dB, is on target, so float rounding never rewrites a sidecar.</summary>
    private const double ToleranceDb = 0.05;

    public LoudnessOptions Options => options();

    /// <summary>Null until the file is read, or when ffmpeg found no loudness in it.</summary>
    public float? For(string audioPath) =>
        reader.Read(audioPath) is { } reading ? Wanted(audioPath, reading, options()) : null;

    /// <summary>As <see cref="For"/>, reading the file first when it has no current reading.</summary>
    public async Task<float?> MeasureAsync(string audioPath, CancellationToken ct) =>
        (reader.Read(audioPath) ?? await reader.MeasureAsync(audioPath, ct)) is { } reading
            ? Wanted(audioPath, reading, options())
            : null;

    public static bool IsOnTarget(float gain, float wanted) =>
        Math.Abs(Decibels(gain) - Decibels(wanted)) <= ToleranceDb;

    internal static float? Wanted(
        string audioPath,
        LoudnessReading reading,
        LoudnessOptions options
    ) =>
        reading.IntegratedLufs switch
        {
            null => null,
            // A warping guide the user rarely hears.
            _ when StagedFile.IsWarpGuidePreset(StemTagReader.PresetOf(audioPath)) => 1,
            // Separator residue, which a gain would only amplify.
            { } lufs when lufs < options.FloorLufs => 1,
            { } lufs => (float)Math.Pow(10, (options.TargetLufs - lufs) / 20),
        };

    private static double Decibels(float gain) => 20 * Math.Log10(gain);
}

/// <summary>The <c>Loudness</c> section of appsettings: what every sample's clip gain aims at (ADR 0059).</summary>
public sealed class LoudnessOptions
{
    public const string SectionName = "Loudness";

    /// <summary>The integrated loudness every levelled sample is brought to, in LUFS.</summary>
    public double TargetLufs { get; set; } = -14;

    /// <summary>A sample quieter than this, in LUFS integrated, is left at 0 dB.</summary>
    public double FloorLufs { get; set; } = -24;
}
