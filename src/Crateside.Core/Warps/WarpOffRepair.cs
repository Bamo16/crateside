using AbleKit.Analysis;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// Gives every saved clip with Warp off and fewer than two markers the pair Live writes, which Live
/// crashes without (ADR 0091). Backs each sidecar up first and leaves one an open set uses for later.
/// </summary>
public sealed class WarpOffRepair(
    CratesidePaths paths,
    SidecarWriter writer,
    SidecarBackups backups,
    ISetLinks sets,
    BeatReader beats,
    LiveAnalyses waiting
)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<string>? _deferred;

    /// <summary>Whether a pass has found nothing left to repair.</summary>
    public bool IsDone
    {
        get
        {
            lock (_lock)
            {
                return _deferred is [];
            }
        }
    }

    /// <summary>
    /// The first pass reads every sidecar in staging and the library; later ones only the files an
    /// open set held back. The samples repaired and the samples left for later.
    /// </summary>
    public (IReadOnlyList<string> Repaired, IReadOnlyList<string> Deferred) Run()
    {
        IReadOnlyList<string> candidates;

        lock (_lock)
        {
            candidates = _deferred ?? [.. Samples().Where(NeedsMarkers)];
        }

        var open = candidates.Count > 0 ? Held(candidates) : [];
        List<string> repaired = [];
        List<string> deferred = [];

        foreach (var path in candidates)
        {
            if (waiting.Of(path) is not null || open.Contains(path))
            {
                deferred.Add(path);

                continue;
            }

            if (Repair(path))
                repaired.Add(path);
            else if (NeedsMarkers(path))
                deferred.Add(path);
        }

        lock (_lock)
        {
            _deferred = deferred;
        }

        return (repaired, deferred);
    }

    private bool Repair(string audioPath)
    {
        try
        {
            backups.Keep(StemFileName.AnalysisFor(audioPath));

            return writer.GiveWarpOffMarkers(audioPath, TempoOf(audioPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The track's estimated tempo when crateside has one; it only spaces the pair.</summary>
    private double? TempoOf(string audioPath) =>
        StarterGrid
            .Of(beats.ReadFor(StemTagReader.Read(audioPath)?.SourceTrackKey, audioPath))
            ?.Bpm;

    /// <summary>The candidates a set Live has open uses; all of them while Live is running and says no set.</summary>
    private HashSet<string> Held(IReadOnlyList<string> candidates)
    {
        if (sets.Using(candidates).Count is 0)
            return [];

        return sets.ReadOpenSet() switch
        {
            { IsRunning: false } => [],
            { Set: null } => [.. candidates],
            { Set: { } open } =>
            [
                .. candidates.Where(path =>
                    sets.Using([path]).Contains(open, StringComparer.OrdinalIgnoreCase)
                ),
            ],
        };
    }

    private static bool NeedsMarkers(string audioPath) =>
        AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
        && analysis is { IsDefaultClipSaved: true, Warp: { IsWarped: false, Markers.Count: < 2 } };

    private IEnumerable<string> Samples() =>
        Audio(paths.StagingFolder, SearchOption.TopDirectoryOnly)
            .Concat(Audio(paths.LibraryFolder, SearchOption.AllDirectories));

    private static IEnumerable<string> Audio(string root, SearchOption depth) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", depth).Where(SampleFiles.IsAudio)
            : [];
}
