using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Ledger;

namespace Crateside.Core.Pairing;

/// <summary>A key that pairs with a source center, at the shift that gets it there.</summary>
public sealed record MashupPartner
{
    public required TonalCenter Source { get; init; }

    /// <summary>One of the 24 keys, or a modal source's own center.</summary>
    public required TonalCenter Key { get; init; }

    /// <summary>Named only at shift 0; null for every other partner.</summary>
    public Relationship? Relationship { get; init; }

    /// <summary>Whether this is a modal source's tag, its collapse: <c>A minor</c> for A Dorian.</summary>
    public bool IsTag { get; init; }

    /// <summary>Semitones the partner must move to sit with the source, signed, within ±6.</summary>
    public required int Shift { get; init; }

    /// <summary>The other transpositions reaching this partner, a fifth away either way.</summary>
    public required IReadOnlyList<int> AltShifts { get; init; }

    /// <summary>How many of the source's seven notes the partner holds once moved by its shift.</summary>
    public required int NotesShared { get; init; }

    /// <summary>The key Ableton's browser filters by: the partner's collapse.</summary>
    public MusicKey Tag => MusicKey.Of(Key);

    public string CamelotCode => Tag.CamelotCode;

    public int AbsoluteShift => Math.Abs(Shift);

    public string AltShiftsDisplay => string.Join(", ", AltShifts.Select(FormatShift));

    /// <summary>Formats a shift for display, with a leading <c>+</c> and a real minus.</summary>
    public static string FormatShift(int shift) =>
        shift switch
        {
            > 0 => $"+{shift}",
            < 0 => $"−{-shift}",
            _ => "0",
        };

    /// <summary>The partner at its shift closest to 0 within the range; false when none of its shifts is in it.</summary>
    public static bool TryCreate(
        TonalCenter source,
        TonalCenter candidate,
        int lowest,
        int highest,
        [NotNullWhen(true)] out MashupPartner? partner
    )
    {
        var sameKey = candidate == source;
        var sameNotes = !sameKey && Scale.Notes(candidate) == Scale.Notes(source);

        // A key holding the source's notes, or its tag's, needs no shift; any other moves its tonic onto the source's.
        var unmoved = sameKey || sameNotes || Scale.Notes(candidate) == Scale.Notes(source.Plain);
        var difference = unmoved ? 0 : Normalize(candidate.Chromatic - source.Chromatic);

        int[] shifts =
        [
            .. new[]
            {
                Normalize(-difference),
                Normalize(-difference + 7),
                Normalize(-difference - 7),
            }.Distinct(),
        ];

        if (
            shifts.Where(shift => shift >= lowest && shift <= highest).OrderBy(Math.Abs).ToList()
            is not [var listed, ..]
        )
        {
            partner = null;

            return false;
        }

        partner = new MashupPartner
        {
            Source = source,
            Key = candidate,
            Relationship = (listed, sameKey, sameNotes) switch
            {
                (0, true, _) => Pairing.Relationship.SameKey,
                (0, _, true) => Pairing.Relationship.SameNotes,
                _ => null,
            },
            IsTag = source.IsModal && candidate == source.Plain,
            Shift = listed,
            AltShifts = [.. shifts.Where(shift => shift != listed).OrderBy(Math.Abs)],
            NotesShared = int.PopCount(Scale.Notes(source) & Scale.Notes(candidate, listed)),
        };

        return true;
    }

    /// <summary>Wraps a semitone difference into ±6, the shorter way round.</summary>
    private static int Normalize(int semitones)
    {
        while (semitones > 6)
            semitones -= 12;

        while (semitones < -6)
            semitones += 12;

        return semitones;
    }
}
