using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Decides which track's stems to hand off next, by reading the stems root rather than the queue.
/// </summary>
public sealed class HandoffSelector(
    JobStore jobs,
    CratesidePaths paths,
    int maxStrikes = HandoffSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 3;

    public bool TryPickNext(out string trackKey, out IReadOnlyList<string> stems)
    {
        trackKey = null!;
        stems = [];

        if (!Directory.Exists(paths.StemsKeyIdentified))
            return false;

        var byTrackKey = Directory
            .EnumerateFiles(paths.StemsKeyIdentified)
            .Select(StemTagReader.Read)
            .OfType<StemTags>()
            .Where(t => t.SourceTrackKey is { Length: > 0 })
            .GroupBy(t => t.SourceTrackKey!, StringComparer.Ordinal)
            .FirstOrDefault(g => IsEligible(g.Key));

        if (byTrackKey is null)
            return false;

        trackKey = byTrackKey.Key;
        stems = [.. byTrackKey.Select(t => t.Path)];

        return true;
    }

    private bool IsEligible(string trackKey) =>
        SeparationOwesNothingMore(trackKey)
        && StageEligibility.Allows(
            jobs,
            trackKey,
            JobStage.Handoff,
            maxStrikes,
            retrySucceeded: true
        );

    /// <summary>
    /// Whether what is on disk for this track is all there is going to be: not while its
    /// separation failed, or until it is settled, which an ended process is not yet (ADR 0042).
    /// </summary>
    private bool SeparationOwesNothingMore(string trackKey) =>
        !jobs.TryGet(trackKey, JobStage.Separation, out var separation)
        || separation is { IsOpen: false, Status: not JobStatus.Failed };
}
