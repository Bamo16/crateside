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

        var next = Groups().FirstOrDefault(g => WhyNot(g.Key) is null);

        if (next is null)
            return false;

        trackKey = next.Key;
        stems = [.. next.Select(t => t.Path)];

        return true;
    }

    /// <summary>Every track with stems waiting on disk, with why the handoff will or will not take it.</summary>
    public IReadOnlyList<Decision> Decisions() =>
        [.. Groups().Select(g => new Decision(g.Key, g.Key, Selection.From(WhyNot(g.Key))))];

    private IEnumerable<IGrouping<string, StemTags>> Groups() =>
        Directory.Exists(paths.StemsKeyIdentified)
            ? Directory
                .EnumerateFiles(paths.StemsKeyIdentified)
                .Select(StemTagReader.Read)
                .OfType<StemTags>()
                .Where(t => t.SourceTrackKey is { Length: > 0 })
                .GroupBy(t => t.SourceTrackKey!, StringComparer.Ordinal)
            : [];

    private SkipCause? WhyNot(string trackKey) =>
        !SeparationOwesNothingMore(trackKey)
            ? SkipCause.SeparationNotSettled
            : StageEligibility.WhyNot(
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
