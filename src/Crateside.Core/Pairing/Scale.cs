using Crateside.Core.Ledger;

namespace Crateside.Core.Pairing;

/// <summary>A tonal centre's seven notes as a 12-bit set of pitch classes, C at bit 0.</summary>
internal static class Scale
{
    private static readonly int[] Ionian = [0, 2, 4, 5, 7, 9, 11];

    public static int Notes(TonalCentre centre, int shift = 0)
    {
        var degree = (int)centre.Mode;
        var notes = 0;

        for (var i = 0; i < 7; i++)
        {
            var step = Ionian[(degree + i) % 7] - Ionian[degree];
            notes |= 1 << (((centre.Chromatic + step + shift) % 12 + 12) % 12);
        }

        return notes;
    }
}
