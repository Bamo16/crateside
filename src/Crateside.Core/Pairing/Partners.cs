namespace Crateside.Core.Pairing;

/// <summary>
/// "What else can I play over this?" — the partners for a source key, ranked.
/// </summary>
/// <remarks>Returns keys, not files; the filtering happens in Ableton's own browser.</remarks>
public static class Partners
{
    /// <summary>The widest shift the model expresses, since shifts normalize to ±6.</summary>
    public const int MaxPossibleShift = 6;

    /// <summary>
    /// Every partner for <paramref name="source"/>, ordered by absolute shift first, then score.
    /// </summary>
    /// <param name="maxShift">
    /// Narrows the results to partners reachable within this many semitones. Defaults to all
    /// 24 keys.
    /// </param>
    public static IReadOnlyList<MashupPartner> For(MusicKey source, int maxShift = MaxPossibleShift)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxShift);

        return
        [
            .. MusicKey
                .AllKeys.Select(candidate => MashupPartner.Create(source, candidate))
                .Where(partner => partner.AbsoluteShift <= maxShift)
                .OrderBy(partner => partner.AbsoluteShift)
                .ThenBy(partner => partner.Score),
        ];
    }

    /// <summary>The detail for one specific pairing, without ranking anything.</summary>
    public static MashupPartner Between(MusicKey source, MusicKey partner) =>
        MashupPartner.Create(source, partner);
}
