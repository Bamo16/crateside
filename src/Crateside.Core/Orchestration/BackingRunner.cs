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
    /// Every file under <paramref name="root"/> whose key is not backed and could be, with what
    /// its entry should say.
    /// </summary>
    /// <remarks>
    /// A key already in the store is never rewritten — that is where the user's own hand-applied
    /// tags land (ADR 0023).
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
                .Where(StagingBoard.IsStemFile)
                .Select(path => new
                {
                    Path = path,
                    Existing = backed.TryGet(path, out var tags) ? tags : null,
                })
                // Before reading any provenance, which costs a tag read per file.
                .Where(file => NeedsAKey(file.Existing))
                .Select(file => new
                {
                    file.Path,
                    file.Existing,
                    Keywords = KeywordsFor(file.Path, ledger),
                })
                .Where(file => file.Keywords.Count > 0)
                .Select(file => new
                {
                    file.Path,
                    Keywords = Merged(file.Existing, file.Keywords),
                    Had = file.Existing?.Keywords.Count ?? 0,
                })
                // A write that adds nothing is not worth a reconcile — and one whose whole key
                // the user had taken off adds nothing.
                .Where(file => file.Keywords.Count > file.Had)
                .Select(file => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(root, file.Path),
                    Keywords = file.Keywords,
                }),
        ];
    }

    /// <summary>
    /// Whether this file's entry is missing a key that writing one would supply.
    /// </summary>
    /// <remarks>
    /// Half a key is left alone: the audit reports it, and completing one is a guess about which
    /// half is right rather than filling a blank.
    /// </remarks>
    internal static bool NeedsAKey(AbletonFileTags? existing) =>
        existing is null
        || (
            existing is { Key: null, HasPartialKey: false }
            // A keyword the writer does not know would be refused, taking the whole pass with it,
            // and dropping it to get the write through would lose what the user put there.
            && existing.Keywords.All(AbletonFolderInfoWriter.IsValidKeyword)
        );

    /// <summary>
    /// The keywords to write: everything the entry already has, plus the key it lacks.
    /// </summary>
    /// <remarks>
    /// The writer replaces an entry's whole keyword bag, so anything not carried across here is
    /// destroyed. A <c>Type</c> the user set wins over the one the preset implies, and a keyword
    /// they took off is never put back.
    /// </remarks>
    internal static IReadOnlyList<string> Merged(
        AbletonFileTags? existing,
        IReadOnlyList<string> keywords
    ) =>
        existing is null
            ? keywords
            :
            [
                .. existing.Keywords,
                .. keywords.Where(keyword =>
                    !existing.HiddenKeywords.Contains(keyword, StringComparer.Ordinal)
                    && (
                        keyword.StartsWith("Key|", StringComparison.Ordinal)
                        || (
                            existing.Type is null
                            && keyword.StartsWith("Type|", StringComparison.Ordinal)
                        )
                    )
                ),
            ];

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
