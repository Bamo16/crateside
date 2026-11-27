using System.Diagnostics;
using System.Globalization;
using Crateside.Core.Ledger;

namespace Crateside.Core.Ableton;

/// <summary>The Camelot group crateside derives from a file's Key tags: 1–12, and A or B (ADR 0112).</summary>
public static class CamelotTags
{
    public const string Group = "Camelot";

    private static readonly HashSet<string> Values =
    [
        with(StringComparer.Ordinal),
        .. Enumerable.Range(1, 12).Select(number => number.ToString(CultureInfo.InvariantCulture)),
        "A",
        "B",
    ];

    public static bool IsKnown(string value) => Values.Contains(value);

    public static bool IsKeyword(string keyword) =>
        keyword.StartsWith($"{Group}|", StringComparison.Ordinal);

    /// <summary>One number per center's note set, and a letter for each plain major or minor one; a modal center has no letter.</summary>
    public static IReadOnlyList<string> For(IEnumerable<TonalCenter> centers)
    {
        var all = centers.ToArray();

        return
        [
            .. all.Select(NumberOf).Distinct().Order().Select(number => $"{Group}|{number}"),
            .. all.Where(center => !center.IsModal)
                .Select(center => TonalCenter.IsMajor(center.Mode) ? "B" : "A")
                .Distinct()
                .Order()
                .Select(letter => $"{Group}|{letter}"),
        ];
    }

    /// <summary>The bag with its Camelot tags replaced by the ones its Key tags call for; a bag with no whole key is left as it is.</summary>
    public static IReadOnlyList<string> Reconcile(IReadOnlyList<string> keywords)
    {
        if (TonalCenter.FromKey(FileTagsReading.ReadKey(keywords).Key) is not { } plain)
            return keywords;

        var center = keywords
            .Select(keyword => keyword.Split('|'))
            .Where(parts => parts is ["Key", var value] && ModeTags.IsModal(value))
            .Select(parts => plain with { Mode = Enum.Parse<DiatonicMode>(parts[1]) })
            .FirstOrDefault(plain);

        return [.. keywords.Where(keyword => !IsKeyword(keyword)), .. For([center])];
    }

    private static int NumberOf(TonalCenter center) =>
        (((center.Chromatic - SemitonesAboveMajor(center.Mode) + 12) % 12 * 7 % 12) + 7) % 12 + 1;

    private static int SemitonesAboveMajor(DiatonicMode mode) =>
        mode switch
        {
            DiatonicMode.Ionian => 0,
            DiatonicMode.Dorian => 2,
            DiatonicMode.Phrygian => 4,
            DiatonicMode.Lydian => 5,
            DiatonicMode.Mixolydian => 7,
            DiatonicMode.Aeolian => 9,
            DiatonicMode.Locrian => 11,
            _ => throw new UnreachableException(),
        };
}
