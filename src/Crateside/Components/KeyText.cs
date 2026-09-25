using Crateside.Core.Ledger;

namespace Crateside.Components;

/// <summary>How a verdict's key and confidence read on screen.</summary>
public static class KeyText
{
    // The ledger spells keys in ASCII so they survive a filename. The screen is not one.
    public static string? Spell(string? key) =>
        key
            ?.Replace("#", "♯", StringComparison.Ordinal)
            .Replace(" major", " maj", StringComparison.Ordinal)
            .Replace(" minor", " min", StringComparison.Ordinal);

    public static string Confidence(KeyConfidence confidence) =>
        confidence is KeyConfidence.Unknown ? "—" : confidence.ToString().ToUpperInvariant();

    public static string ConfidenceTone(KeyConfidence confidence) =>
        confidence switch
        {
            KeyConfidence.High => "high",
            KeyConfidence.Medium => "medium",
            KeyConfidence.Low or KeyConfidence.Withheld => "weak",
            _ => "none",
        };
}
