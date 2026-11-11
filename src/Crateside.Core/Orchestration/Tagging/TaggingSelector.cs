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
        out string trackKey,
        out IReadOnlyList<TaggedStem> stems
    )
    {
        trackKey = null!;
        stems = [];

        var next = Groups(ledger).FirstOrDefault(g => WhyNot(g.Key) is null);

        if (next is null)
            return false;

        trackKey = next.Key;
        stems = [.. next];

        return true;
    }

    /// <summary>Every track with staged files owing a key, with why tagging will or will not take it.</summary>
    public IReadOnlyList<Decision> Decisions(KeyLedger ledger) =>
        [.. Groups(ledger).Select(g => new Decision(g.Key, g.Key, Selection.From(WhyNot(g.Key))))];

    private IEnumerable<IGrouping<string, TaggedStem>> Groups(KeyLedger ledger) =>
        TagAudit
            .Run(
                ledger,
                [AuditRoot.Written(paths.StagingFolder)],
                TagAudit.Using(index, folderInfo)
            )
            .Untagged.GroupBy(s => s.SourceTrackKey, StringComparer.Ordinal);

    private SkipCause? WhyNot(string trackKey) =>
        StageEligibility.WhyNot(jobs, trackKey, JobStage.Tagging, maxStrikes, retrySucceeded: true);
}
