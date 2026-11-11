using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>Decides which track to detect next.</summary>
public sealed class EvidenceSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = EvidenceSelector.DefaultMaxStrikes
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 5;

    /// <summary>
    /// The next track to detect: oldest-added first, skipping anything with evidence already, in
    /// flight, or given up on.
    /// </summary>
    public bool TryPickNext(WorkListsView view, out WorkItem next)
    {
        next = view.Outstanding.FirstOrDefault(i => WhyNot(i) is null)!;

        return next is not null;
    }

    /// <summary>Every outstanding track, with why detection will or will not take it.</summary>
    public IReadOnlyList<Decision> Decisions(WorkListsView view) =>
        [
            .. view.Outstanding.Select(i => new Decision(
                i.TrackKey,
                i.Title,
                Selection.From(WhyNot(i))
            )),
        ];

    private SkipCause? WhyNot(WorkItem i) =>
        i.IsUnavailable ? SkipCause.Unavailable
        : workingSet.HasEvidence(i.TrackKey) ? SkipCause.AlreadyHasEvidence
        // Not this stage's turn until the download stage has been (ADR 0019). Picking one up
        // early costs it a strike for arriving before its audio, which is not a fault of the
        // track and would eventually make it give up.
        : !workingSet.TryGetSource(i.TrackKey, out _) ? SkipCause.NoAudioYet
        // The file lands before stemforge-cli tags it, and a detector holding it open makes
        // that write fail silently: stems cut from it carry no source and join to nothing.
        : IsDownloading(i.TrackKey) ? SkipCause.Downloading
        // Retries even a succeeded detection, since the caller has already found its evidence gone.
        : StageEligibility.WhyNot(
            jobs,
            i.TrackKey,
            JobStage.Evidence,
            maxStrikes,
            retrySucceeded: true
        );

    private bool IsDownloading(string trackKey) =>
        jobs.TryGet(trackKey, JobStage.Download, out var download) && download.IsOpen;
}
