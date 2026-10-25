using Crateside.Core.Ledger;

namespace Crateside.Core.Ableton;

/// <summary>The mode tag crateside adds in Live's Key group beside the collapse (#127).</summary>
public static class ModeTags
{
    private static readonly HashSet<string> Modal = new(StringComparer.Ordinal)
    {
        nameof(DiatonicMode.Dorian),
        nameof(DiatonicMode.Phrygian),
        nameof(DiatonicMode.Lydian),
        nameof(DiatonicMode.Mixolydian),
        nameof(DiatonicMode.Locrian),
    };

    public static bool IsModal(string value) => Modal.Contains(value);

    public static bool IsKeyword(string keyword) =>
        keyword.Split('|') is ["Key", var value] && IsModal(value);

    /// <summary><c>Key|Dorian</c> for a modal center; null where the collapse says it all.</summary>
    public static string? KeywordFor(TonalCenter? center) =>
        center is { IsModal: true, Mode: var mode } ? $"Key|{mode}" : null;
}
