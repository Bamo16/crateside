using Crateside.Core.Pairing;
using Humanizer;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>How the keys in play relate, computed so the judge never works it out from the key names (ADR 0095).</summary>
internal static class KeyRelations
{
    private const int MostKeys = 6;
    private const int ScaleNotes = 7;

    private static readonly int[] MajorSteps = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] MinorSteps = [0, 2, 3, 5, 7, 8, 10];

    /// <summary>The work order's lines on how the keys relate, one per pair that shares most of its notes or a tonic; none for fewer than two keys.</summary>
    public static IEnumerable<string> Lines(IEnumerable<MusicKey> keys) =>
        keys.Distinct().Take(MostKeys).ToList() is { Count: > 1 } played
            ?
            [
                string.Empty,
                "How the keys in the evidence relate, computed by crateside from each key's seven "
                    + "scale notes:",
                .. Pairs(played).Select(Describe).OfType<string>(),
            ]
            : [];

    /// <summary>How two keys relate, or null for a pair sharing fewer than five notes with different tonics.</summary>
    public static string? Describe((MusicKey First, MusicKey Second) pair)
    {
        var shared = Notes(pair.First).Intersect(Notes(pair.Second)).Count();
        var relation =
            shared == ScaleNotes && pair.First.Chromatic != pair.Second.Chromatic
                ? "relative: the same notes"
            : pair.First.Chromatic == pair.Second.Chromatic ? "parallel: the same tonic"
            : shared >= ScaleNotes - 2 ? "neighbors"
            : null;

        return relation is null
            ? null
            : $"- {pair.First.LedgerName} and {pair.Second.LedgerName}: {relation}, "
                + $"{"note".ToQuantity(shared)} in common of {ScaleNotes}";
    }

    private static IEnumerable<(MusicKey, MusicKey)> Pairs(List<MusicKey> keys) =>
        keys.SelectMany((first, index) => keys.Skip(index + 1).Select(second => (first, second)));

    private static HashSet<int> Notes(MusicKey key) =>
        [
            .. (key.Mode is Mode.Major ? MajorSteps : MinorSteps).Select(step =>
                (key.Chromatic + step) % 12
            ),
        ];
}
