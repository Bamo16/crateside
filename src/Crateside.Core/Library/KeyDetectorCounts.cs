using Crateside.Core.Ledger;
using Crateside.Core.Pairing;

namespace Crateside.Core.Library;

/// <summary>How often each key detector agrees with the verdict, and how its misses relate to it (#78).</summary>
public sealed record KeyDetectorCounts(
    int Songs,
    IReadOnlyList<KeyDetectorTally> Detectors,
    IReadOnlyList<AllThreeWrong> AllWrong
)
{
    /// <summary>The detectors one run has: keydetect, madmom and edma.</summary>
    private const int DetectorsPerRun = 3;

    /// <summary>
    /// The detectors crateside still runs, in the ledger's own names and the order the panel shows them
    /// (#113). CREMA and librosa ran before the port; their readings stay in older ledger runs and are not shown.
    /// </summary>
    public static readonly IReadOnlyList<(string Ledger, string Shown, string Who)> Names =
    [
        ("ffmpeg keydetect", "keydetect", "FiLL's ffmpeg filter"),
        ("madmom", "madmom", "CNN, ONNX"),
        ("Essentia edma", "edma", "HPCP profile"),
    ];

    public static KeyDetectorCounts From(
        IEnumerable<RunEvidence> latest,
        KeyLedger ledger,
        bool highOnly
    )
    {
        var judged = latest
            // The verdict from the latest run; the readings from the latest run that has any, since
            // a verdict recorded by hand carries no detectors.
            .Select(evidence =>
                ledger.TryGetLatestBySong(evidence.Song, out var run)
                    ? (Evidence: evidence, Run: run, Verdict: MusicKey.FromString(run.Key))
                    : (Evidence: evidence, Run: null, Verdict: null)
            )
            .Where(song =>
                song is { Run: { } run, Verdict: not null }
                && (
                    run.Confidence is KeyConfidence.High
                    || !highOnly && run.Confidence is KeyConfidence.Medium
                )
                && song.Evidence.Algorithms.Count > 0
            )
            .ToList();

        // A detector no song has a reading from yet is left out of the panel.
        var shown = Names
            .Where(name =>
                judged.Any(song =>
                    song.Evidence.Algorithms.Any(result => result.Name == name.Ledger)
                )
            )
            .ToList();

        var readings = judged
            .Select(song =>
                (
                    song.Evidence,
                    song.Run,
                    song.Verdict,
                    Reads: shown
                        .Select(name =>
                            MusicKey.FromString(
                                song.Evidence.Algorithms.FirstOrDefault(result =>
                                    result.Name == name.Ledger
                                )?.Key
                            )
                        )
                        .ToArray()
                )
            )
            .ToList();

        return new KeyDetectorCounts(
            judged.Count,
            [
                .. shown.Select(
                    (name, index) =>
                        new KeyDetectorTally(
                            name.Shown,
                            name.Who,
                            readings
                                .Select(song => KeyMisses.Of(song.Reads[index], song.Verdict!))
                                .CountBy(miss => miss)
                                .ToDictionary()
                        )
                ),
            ],
            [
                .. readings
                    .Where(song =>
                        song.Reads.Count(read => read is not null) is DetectorsPerRun
                        && song.Reads.All(read => read is null || !read.Equals(song.Verdict))
                    )
                    .Select(song => new AllThreeWrong(
                        song.Evidence.DisplayName,
                        song.Verdict!,
                        song.Run!.Confidence,
                        song.Reads
                    ))
                    .OrderBy(song => song.Song, StringComparer.OrdinalIgnoreCase),
            ]
        );
    }
}

/// <summary>One detector's readings, counted by how each relates to the verdict.</summary>
public sealed record KeyDetectorTally(
    string Name,
    string Who,
    IReadOnlyDictionary<KeyMiss, int> ByMiss
)
{
    public int Count(KeyMiss miss) => ByMiss.GetValueOrDefault(miss);

    /// <summary>Right, a fifth, or the relative: close enough to pair for a mashup.</summary>
    public int RightOrClose =>
        Count(KeyMiss.Right) + Count(KeyMiss.Fifth) + Count(KeyMiss.Relative);

    public int Read =>
        ByMiss.Where(pair => pair.Key is not KeyMiss.NoReading).Sum(pair => pair.Value);

    public int Misses =>
        ByMiss.Where(pair => pair.Key is not KeyMiss.Right).Sum(pair => pair.Value);
}

/// <summary>A song all three of its run's detectors read, and none read as the verdict.</summary>
public sealed record AllThreeWrong(
    string Song,
    MusicKey Verdict,
    KeyConfidence Confidence,
    IReadOnlyList<MusicKey?> Reads
);

/// <summary>How a detector's key relates to the verdict.</summary>
public enum KeyMiss
{
    Right,
    Fifth,
    Relative,
    Parallel,
    Other,
    NoReading,
}

public static class KeyMisses
{
    public static KeyMiss Of(MusicKey? read, MusicKey verdict) =>
        read switch
        {
            null => KeyMiss.NoReading,
            _ when read.Equals(verdict) => KeyMiss.Right,
            _ when read.Mode != verdict.Mode && read.CirclePos == verdict.CirclePos =>
                KeyMiss.Relative,
            _ when read.Mode == verdict.Mode
                    && (read.CirclePos - verdict.CirclePos + 12) % 12 is 1 or 11 => KeyMiss.Fifth,
            _ when read.Mode != verdict.Mode && read.Chromatic == verdict.Chromatic =>
                KeyMiss.Parallel,
            _ => KeyMiss.Other,
        };
}
