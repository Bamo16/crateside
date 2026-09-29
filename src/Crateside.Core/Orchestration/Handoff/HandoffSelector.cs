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

    public bool TryPickNext(out string videoId, out IReadOnlyList<string> stems)
    {
        videoId = null!;
        stems = [];

        if (!Directory.Exists(paths.StemsKeyIdentified))
            return false;

        var byVideoId = Directory
            .EnumerateFiles(paths.StemsKeyIdentified)
            .Select(StemTagReader.Read)
            .OfType<StemTags>()
            .Where(t => t.SourceVideoId is { Length: > 0 })
            .GroupBy(t => t.SourceVideoId!, StringComparer.Ordinal)
            .FirstOrDefault(g => IsEligible(g.Key));

        if (byVideoId is null)
            return false;

        videoId = byVideoId.Key;
        stems = [.. byVideoId.Select(t => t.Path)];

        return true;
    }

    private bool IsEligible(string videoId) =>
        SeparationOwesNothingMore(videoId)
        && StageEligibility.Allows(
            jobs,
            videoId,
            JobStage.Handoff,
            maxStrikes,
            retrySucceeded: true
        );

    /// <summary>
    /// Whether what is on disk for this track is all there is going to be: not while its
    /// separation failed, or until it is settled, which an ended process is not yet (ADR 0042).
    /// </summary>
    private bool SeparationOwesNothingMore(string videoId) =>
        !jobs.TryGet(videoId, JobStage.Separation, out var separation)
        || separation is { IsOpen: false, Status: not JobStatus.Failed };
}
