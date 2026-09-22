using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Gives every file in a root that has no tag-store entry the one its verdict earns, so its key
/// survives the next write to that root (ADR 0023).
/// </summary>
public sealed class BackingRunner(
    AbletonFolderInfoReader folderInfo,
    AbletonFolderInfoWriter writer
)
{
    /// <summary>Backs everything under <paramref name="root"/> that is not backed yet.</summary>
    /// <remarks>
    /// One <c>Apply</c> for the whole root, never one per file: each write reconciles the root's
    /// index down to the store, so a second write would destroy what the first had not reached.
    /// </remarks>
    public TagWriteOutcome Run(string root, KeyLedger ledger) =>
        writer.Apply(root, Unbacked(root, ledger));

    /// <summary>
    /// Every file under <paramref name="root"/> that has no store entry and could have one, with
    /// what that entry should say.
    /// </summary>
    /// <remarks>
    /// Files that already have an entry are left alone, whatever it says — the store is where the
    /// user's own hand-applied tags land (ADR 0023).
    /// </remarks>
    public IReadOnlyList<TagAssignment> Unbacked(string root, KeyLedger ledger)
    {
        if (!Directory.Exists(root))
            return [];

        var backed = folderInfo.Read(root);

        return
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => StagingBoard.IsStemFile(path) && !backed.TryGet(path, out _))
                .Select(path => new { Path = path, Keywords = KeywordsFor(path, ledger) })
                .Where(file => file.Keywords.Count > 0)
                .Select(file => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(root, file.Path),
                    Keywords = file.Keywords,
                }),
        ];
    }

    /// <summary>
    /// What one file's entry should say, from its own provenance and the verdict the ledger
    /// settled. Empty for a file nothing can be said about.
    /// </summary>
    private static IReadOnlyList<string> KeywordsFor(string path, KeyLedger ledger) =>
        StemTagReader.Read(path)?.Provenance is { SourceVideoId: { } videoId } provenance
        && ledger.TryGetLatestByVideoId(videoId, out var run)
            ? TaggingRunner.KeywordsFor(run.Key, provenance.Preset)
            : [];
}
