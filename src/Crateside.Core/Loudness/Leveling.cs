using System.Collections.Concurrent;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;
using Crateside.Core.Warps;

namespace Crateside.Core.Loudness;

/// <summary>
/// Reads every sample's loudness in staging and the library, and keeps each saved default clip at
/// its clip gain, the user's included; one with Live (#97) waits until Live is done (ADR 0059).
/// </summary>
public sealed class Leveling(
    CratesidePaths paths,
    LoudnessReader reader,
    ClipGains gains,
    SidecarWriter writer,
    LiveAnalyses waiting,
    StageNotes? notes = null
)
{
    private const int AtOnce = 8;

    /// <summary>The sidecar's stamp and the gain it was last checked against, so an unchanged one is not read again.</summary>
    private readonly ConcurrentDictionary<string, (string Stamp, float Gain)> _settled = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>The samples without a current reading, staging's first.</summary>
    public IReadOnlyList<string> Unread() =>
        [.. Samples().Where(path => reader.Read(path) is null)];

    public Task ReadAsync(IReadOnlyList<string> unread, CancellationToken ct) =>
        Parallel.ForEachAsync(
            unread,
            new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = ct },
            async (path, token) =>
            {
                await reader.MeasureAsync(path, token);
                reader.Save();
            }
        );

    /// <summary>Sets the clip gain of every saved sidecar that is off it; the samples rewritten.</summary>
    public IReadOnlyList<string> Level()
    {
        List<string> leveled = [];
        var target = gains.Options.TargetLufs;

        foreach (var path in Samples())
        {
            if (
                waiting.Of(path) is not null
                || gains.For(path) is not { } gain
                || Stamp(path) is not { } stamp
                || _settled.TryGetValue(path, out var settled) && settled == (stamp, gain)
            )
                continue;

            if (writer.Level(path, gain))
            {
                leveled.Add(path);

                if (StemTagReader.Read(path)?.SourceTrackKey is { } trackKey)
                    notes?.Record(trackKey, "Loudness", ClipGainSummary.Of(path, gain, target));
            }

            // A sidecar that could not be written is locked by Live, whose write changes its stamp.
            if (Stamp(path) is { } after)
                _settled[path] = (after, gain);
        }

        return leveled;
    }

    public LoudnessSpread Spread() => LoudnessSpread.From(Samples(), reader, gains);

    private IEnumerable<string> Samples() =>
        Audio(paths.StagingFolder, SearchOption.TopDirectoryOnly)
            .Concat(Audio(paths.LibraryFolder, SearchOption.AllDirectories));

    private static IEnumerable<string> Audio(string root, SearchOption depth) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", depth).Where(SampleFiles.IsAudio)
            : [];

    private static string? Stamp(string audioPath) =>
        new FileInfo(StemFileName.AnalysisFor(audioPath)) is { Exists: true } sidecar
            ? $"{sidecar.Length}:{sidecar.LastWriteTimeUtc.Ticks}"
            : null;
}
