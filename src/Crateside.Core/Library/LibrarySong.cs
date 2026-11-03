using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Ledger;
using Crateside.Core.Pairing;
using Crateside.Core.Tempo;

namespace Crateside.Core.Library;

/// <summary>One finished song in the library, however many samples it has.</summary>
public sealed record LibrarySong
{
    /// <summary>The ledger's song identity, else the upload, else <c>name:</c> and the file's name.</summary>
    public required string Song { get; init; }

    public required string Name { get; init; }

    /// <summary>The first credited artist (ADR 0033), or null when the name credits nobody.</summary>
    public string? Artist { get; init; }

    public MusicKey? Key { get; init; }

    public string? Genre { get; init; }

    /// <summary>The finished warp's tempo, the instrumental's where there is one (ADR 0038).</summary>
    public WarpTempo? Tempo { get; init; }

    /// <summary>beatdetect's reading of the upload's source, else of its instrumental, once crateside has read it (#76, ADR 0065).</summary>
    public BeatReading? Beats { get; init; }

    /// <summary>How the starter warp today's code would write from beatdetect's reading compares with the finished warp; null where they cannot be compared (ADR 0075).</summary>
    public StarterFate? Starter { get; init; }

    public LedgerRun? Run { get; init; }

    /// <summary>What Partners works from: the verdict's primary center, else the samples' Key tag (ADR 0056).</summary>
    public TonalCenter? Center => Run?.Center ?? (Key is { } key ? MusicKey.CenterOf(key) : null);

    /// <summary>The headline tempo: the warp where there is one (ADR 0049).</summary>
    public TrackTempo? Bpm => TrackTempo.Of(Tempo, Beats, Run?.Bpm);
}
