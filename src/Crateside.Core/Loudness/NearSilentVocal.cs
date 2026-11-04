using Crateside.Core.Plans;

namespace Crateside.Core.Loudness;

/// <summary>A vocal stem far quieter than its instrumental belongs to a track with nothing to sing (ADR 0077).</summary>
public static class NearSilentVocal
{
    /// <summary>
    /// How much quieter than the instrumental a vocal stem must be to be flagged, in dB. Four
    /// tracks cut down to their source by hand are the only labelled examples, so revisit it.
    /// </summary>
    public const double ThresholdDb = 16;

    /// <summary>
    /// Whether the loudest vocal stem is more than <see cref="ThresholdDb"/> quieter than the
    /// loudest instrumental, by the preset ids and integrated loudness of the stems measured.
    /// </summary>
    public static bool IsFlagged(IEnumerable<(string? PresetId, double? Lufs)> stems)
    {
        var measured = stems.Where(stem => stem is { PresetId: not null, Lufs: not null }).ToList();

        return Loudest(measured, "vocal") is { } vocal
            && Loudest(measured, "instrumental") is { } instrumental
            && instrumental - vocal > ThresholdDb;
    }

    /// <summary>Whether this plan still asks for a vocal stem, so that flagging it can offer anything.</summary>
    public static bool AsksForVocal(StemPlan plan) =>
        plan.Presets.Any(preset => preset.StartsWith("vocal", StringComparison.Ordinal));

    private static double? Loudest(
        IEnumerable<(string? PresetId, double? Lufs)> stems,
        string family
    ) =>
        stems
            .Where(stem => stem.PresetId!.StartsWith(family, StringComparison.Ordinal))
            .Select(stem => stem.Lufs)
            .Max();
}
