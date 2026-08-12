namespace Crateside.Core.Pairing;

/// <summary>
/// A key that pairs acceptably with a source key, with the cost of getting there.
/// </summary>
/// <remarks>
/// Ported from <c>PSMashup.Models.MashupPartner</c>. The <c>[Hidden]</c>/<c>[Alias]</c>
/// attributes are stripped — they are <c>System.Management.Automation</c> and exist for the
/// cmdlet's table formatting. The static <c>MashupConfig</c> dependency is dropped too: display
/// style is the UI's business, so this carries the key and lets the caller spell it.
/// </remarks>
public sealed record MashupPartner
{
    public required MusicKey Source { get; init; }
    public required MusicKey Key { get; init; }

    /// <summary>What this partner is to the source, musically.</summary>
    public required Relationship Relationship { get; init; }

    /// <summary>
    /// The ranking value: <c>circleDistance * 10 + |shift|</c>, with an exact match scoring 0
    /// and the relative scoring 0.1. Subordinate to <see cref="AbsoluteShift"/> in the sort.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Semitones the partner must move to sit with the source, signed, normalized to ±6.
    /// The dominant cost and the first sort key — an unshifted pairing can be auditioned in
    /// seconds, while anything past ±1–2 usually sounds wrong.
    /// </summary>
    public required int Shift { get; init; }

    /// <summary>
    /// The other transpositions reaching this same partner, a fifth away either way.
    /// Alternatives, not extras: use one <em>instead of</em> the shift when it suits the
    /// material better.
    /// </summary>
    public required IReadOnlyList<int> AltShifts { get; init; }

    public string CamelotCode => Key.CamelotCode;

    public int AbsoluteShift => Math.Abs(Shift);

    /// <summary>Formats a shift for display, with a leading <c>+</c> on positives.</summary>
    public static string FormatShift(int shift) => shift > 0 ? $"+{shift}" : shift.ToString();

    public string AltShiftsDisplay => string.Join(", ", AltShifts.Select(FormatShift));

    public static MashupPartner Create(MusicKey source, MusicKey candidate)
    {
        var circleDistance =
            Math.Abs(candidate.CirclePos - source.CirclePos) is var distance && distance > 6
                ? 12 - distance
                : distance;

        // The relative key is reachable without moving: same notes, other tonic.
        var semitoneDifference =
            circleDistance == 0 && candidate.Mode != source.Mode
                ? 0
                : Normalize(candidate.Chromatic - source.Chromatic);

        int[] potentialShifts =
        [
            .. new[]
            {
                Normalize(-semitoneDifference),
                Normalize(-semitoneDifference + 7),
                Normalize(-semitoneDifference - 7),
            }.Distinct(),
        ];

        var primary = potentialShifts.OrderBy(Math.Abs).First();

        var score = ((circleDistance * 10) + Math.Abs(primary)) switch
        {
            _ when candidate.Equals(source) => 0d,
            0 => 0.1,
            var s => s,
        };

        return new MashupPartner
        {
            Source = source,
            Key = candidate,
            Relationship = GetRelationship(source, candidate, circleDistance),
            Score = score,
            Shift = primary,
            AltShifts = [.. potentialShifts.Where(s => s != primary).OrderBy(Math.Abs)],
        };
    }

    private static Relationship GetRelationship(
        MusicKey source,
        MusicKey candidate,
        int circleDistance
    )
    {
        if (candidate.Equals(source))
            return Relationship.PerfectMatch;

        // Same position on the wheel, different mode — the relative key.
        if (circleDistance == 0 && source.Mode != candidate.Mode)
            return Relationship.Relative;

        if (candidate.Mode == source.Mode && circleDistance == 1)
        {
            // Clockwise brightens, counter-clockwise relaxes.
            var direction = candidate.CirclePos - source.CirclePos;
            return direction is 1 or -11 ? Relationship.EnergyRise : Relationship.Relaxed;
        }

        if (circleDistance == 1 && source.Mode != candidate.Mode)
            return Relationship.MoodShift;

        return Relationship.Modulation;
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
