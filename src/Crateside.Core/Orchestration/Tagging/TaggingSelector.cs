using AbleKit.Tags;
using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Orchestration.Tagging;

/// <summary>
/// Decides whose tags to write next, by asking the audit what is untagged.
/// </summary>
public sealed class TaggingSelector(
    JobStore jobs,
    CratesidePaths paths,
    FileIndexReader index,
    FolderInfoReader folderInfo,
    int maxStrikes = TaggingSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track with staged files that have a verdict and no key applied, read from the XMP
    /// so it terminates (ADR 0017).
    /// </summary>
    public bool TryPickNext(
        KeyLedger ledger,
        out string videoId,
        out IReadOnlyList<TaggedStem> stems
    )
    {
        videoId = null!;
        stems = [];

        var next = TagAudit
            .Run(
                ledger,
                [AuditRoot.Written(paths.StagingFolder)],
                TagAudit.Using(index, folderInfo)
            )
            .Untagged.GroupBy(s => s.SourceVideoId, StringComparer.Ordinal)
            .FirstOrDefault(g => IsEligible(g.Key));

        if (next is null)
            return false;

        videoId = next.Key;
        stems = [.. next];

        return true;
    }

    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Tagging, maxStrikes, retrySucceeded: true);
}
