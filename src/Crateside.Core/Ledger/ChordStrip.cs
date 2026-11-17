namespace Crateside.Core.Ledger;

/// <summary>One block of the chord lane: a chord over a stretch of the track, in seconds.</summary>
public sealed record ChordBand(double Start, double End, string Chord)
{
    /// <summary>The chord as the screen spells it: <c>G♯m</c>.</summary>
    public string Label => JudgeProse.Chord(Chord);
}

/// <summary>Places a run's chords on the track's time axis (#198).</summary>
public static class ChordStrip
{
    /// <summary>
    /// Each chord runs from its start to the next one's, the last to the track's end. <c>N</c>, the filter's
    /// "no chord", leaves a gap. A run with no stored starts draws nothing.
    /// </summary>
    public static IReadOnlyList<ChordBand> Plan(RunEvidence run, double seconds) =>
        run is { Chords: var chords, ChordStarts: var starts } && starts.Count == chords.Count
            ?
            [
                .. chords
                    .Select(
                        (chord, index) =>
                            new ChordBand(
                                starts[index],
                                index + 1 < starts.Count ? starts[index + 1] : seconds,
                                chord
                            )
                    )
                    .Where(band =>
                        band is { Chord: not "N" } && band.End > band.Start && band.Start < seconds
                    )
                    .Select(band => band with { End = Math.Min(band.End, seconds) }),
            ]
            : [];
}
