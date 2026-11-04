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

    /// <summary>The confidence band, or who made the call where it was not the judge's (ADR 0076).</summary>
    public static string Decided(DecidedBy? by, KeyConfidence confidence) =>
        by switch
        {
            DecidedBy.Person => "your call",
            DecidedBy.Rules => $"{Confidence(confidence)} · rules",
            _ => Confidence(confidence),
        };

    /// <summary>Who made the verdict, in a few words.</summary>
    public static string Byline(DecidedBy by) =>
        by switch
        {
            DecidedBy.Person => "your call",
            DecidedBy.Rules => "ruled by crateside",
            _ => "judged",
        };

    public static string ConfidenceTone(KeyConfidence confidence) =>
        confidence switch
        {
            KeyConfidence.High => "high",
            KeyConfidence.Medium => "medium",
            KeyConfidence.Low or KeyConfidence.Withheld => "weak",
            _ => "none",
        };
}
