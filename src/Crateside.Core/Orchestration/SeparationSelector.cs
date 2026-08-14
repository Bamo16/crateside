using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Decides which track to separate next. Split from <see cref="SeparationDrainer"/> because this
/// is the part with the actual rule in it, and it can then be tested without a queue credential,
/// a process or a GPU.
/// </summary>
public sealed class SeparationSelector(
    JobStore jobs,
    int maxStrikes = SeparationSelector.DefaultMaxStrikes
)
{
    /// <summary>
    /// How many failures before a track is left alone. Three, because the failures worth
    /// retrying are transient — a dead download, a busy GPU, a reboot — and one that has failed
    /// three times is failing for a reason retrying will not fix. Past that it costs GPU hours
    /// every night and buries the tracks that would have worked.
    /// </summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to separate: oldest-added first, skipping anything already done, in
    /// flight, or given up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The job store is what makes draining terminate.</strong> A track whose separation
    /// succeeded is still <em>outstanding</em> in queue terms until it has a verdict too — so
    /// selecting on the queue alone would separate the same track forever. Asking what crateside
    /// already ran is the only thing that separates "not done" from "not done yet *by me*".
    /// </para>
    /// <para>
    /// Unavailable items are skipped rather than attempted: the video is deleted or private
    /// upstream, so it can never succeed, and trying would burn its three strikes for nothing.
    /// </para>
    /// </remarks>
    public bool TryPickNext(QueueView view, KeyLedger ledger, out WorkOrder order)
    {
        if (view.Outstanding.FirstOrDefault(i => !i.IsUnavailable && IsEligible(i)) is not { } next)
        {
            order = null!;

            return false;
        }

        order = WorkOrder.Compose(next, ledger);

        return true;
    }

    private bool IsEligible(QueueItem item) =>
        !jobs.TryGet(item.VideoId, JobStage.Separation, out var job)
        || job.Status switch
        {
            // Already separated by us: it is waiting on a verdict, not on the GPU.
            JobStatus.Succeeded => false,
            // Something is on it — or a previous crateside was, and it is being adopted.
            JobStatus.Queued or JobStatus.Running => false,
            // Cancelling is the user's call, not the track's fault, so it is simply next again.
            JobStatus.Cancelled => true,
            _ => job.Strikes < maxStrikes,
        };
}
