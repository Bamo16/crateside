namespace Crateside.Core.Pairing;

/// <summary>
/// "What else can I play over this?" — the partner keys for a source key, ranked; Ableton's
/// browser finds the files.
/// </summary>
public static class Partners
{
    /// <summary>Every key has a partner within this many semitones, whatever the source.</summary>
    public const int WidestShift = 2;

    public const int DefaultMaxShift = 1;

    /// <summary>
    /// Every partner for <paramref name="source"/>, ordered by absolute shift first, then score.
    /// </summary>
    /// <param name="maxShift">
    /// Narrows the results to partners reachable within this many semitones. Defaults to all
    /// 24 keys.
    /// </param>
    public static IReadOnlyList<MashupPartner> For(MusicKey source, int maxShift = WidestShift)
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
