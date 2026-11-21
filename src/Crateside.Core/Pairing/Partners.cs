using System.Diagnostics;
using Crateside.Core.Ledger;

namespace Crateside.Core.Pairing;

/// <summary>
/// "What else can I play over this?": the partner keys for a source center, ranked; Ableton's
/// browser finds the files (ADR 0056).
/// </summary>
public static class Partners
{
    /// <summary>Every key has a partner within this many semitones, whatever the source.</summary>
    public const int WidestShift = 2;

    /// <summary>The farthest any shift goes, either way.</summary>
    public const int FarthestShift = 6;

    /// <summary>Every partner with a shift in the range, by absolute shift, then notes shared.</summary>
    public static IReadOnlyList<MashupPartner> For(
        TonalCenter source,
        int lowest = 0,
        int highest = 0
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lowest, -FarthestShift);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(highest, FarthestShift);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lowest, highest);

        var tag = MusicKey.Of(source);

        return
        [
            .. Candidates(source)
                .Select(candidate =>
                    MashupPartner.TryCreate(source, candidate, lowest, highest, out var partner)
                        ? partner
                        : null
                )
                .OfType<MashupPartner>()
                .OrderBy(partner => partner.AbsoluteShift)
                .ThenByDescending(partner => partner.NotesShared)
                .ThenBy(partner =>
                    partner switch
                    {
                        { Relationship: Relationship.SameKey } => 0,
                        { Relationship: Relationship.SameNotes } => 1,
                        { IsTag: true } => 2,
                        _ => 3,
                    }
                )
                .ThenBy(partner => Distance(tag, partner.Tag))
                .ThenBy(partner => Steps(tag, partner.Tag) > 6)
                .ThenBy(partner => partner.Tag.Mode),
        ];
    }

    /// <summary>One pairing at its smallest shift, without ranking anything.</summary>
    public static MashupPartner Between(TonalCenter source, TonalCenter partner) =>
        MashupPartner.TryCreate(source, partner, -FarthestShift, FarthestShift, out var found)
            ? found
            : throw new UnreachableException();

    /// <summary>What a verdict pairs as: a mixed one as its collapse, since its notes are two scales.</summary>
    public static TonalCenter Basis(TonalCenter center, bool mixed) =>
        mixed ? center.Plain : center;

    private static IEnumerable<TonalCenter> Candidates(TonalCenter source) =>
        source.IsModal
            ? MusicKey.AllKeys.Select(MusicKey.CenterOf).Prepend(source)
            : MusicKey.AllKeys.Select(MusicKey.CenterOf);

    // Clockwise steps round the wheel from one key to another, 0–11.
    private static int Steps(MusicKey from, MusicKey to) =>
        (to.CirclePos - from.CirclePos + 12) % 12;

    private static int Distance(MusicKey from, MusicKey to) =>
        Steps(from, to) is var steps && steps > 6 ? 12 - steps : steps;
}
