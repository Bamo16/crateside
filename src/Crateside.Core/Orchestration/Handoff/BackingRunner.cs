using System.Collections.Concurrent;
using AbletonSampleData;
using Crateside.Core.Ableton;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Gives every unbacked file in a root the entry its verdict earns, so its key survives the next
/// write (ADR 0023), and every keyed entry crateside's own tags: genre (ADR 0030) and familiarity (#52).
/// </summary>
public sealed class BackingRunner(FolderInfoReader folderInfo, FolderInfoWriter writer)
{
    /// <summary>Each file's provenance as last read, keyed by path and good while its write time holds.</summary>
    private readonly ConcurrentDictionary<
        string,
        (DateTime Stamp, StemProvenance? Provenance)
    > _read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Backs everything under <paramref name="root"/> not backed yet, in one write: each write
    /// reconciles the index to the store, so a second would undo the first.
    /// </summary>
    /// <param name="ownedOf">A video ID's keywords in crateside's own groups; a group left out is left alone.</param>
    /// <param name="backKeys">False where another stage writes the keys, which is staging.</param>
    public TagWriteOutcome Run(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf = null,
        bool backKeys = true
    ) => CratesideKeywords.Apply(writer, root, Unbacked(root, ledger, ownedOf, backKeys));

    /// <summary>
    /// Every file under <paramref name="root"/> whose entry is missing a key or a genre it could
    /// have, with what the entry should say; a key already in the store is never rewritten.
    /// </summary>
    public IReadOnlyList<TagAssignment> Unbacked(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf = null,
        bool backKeys = true
    )
    {
        if (!Directory.Exists(root))
            return [];

        var backed = folderInfo.Read(root);

        return
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(SampleFiles.IsAudio)
                .Select(path => new
                {
                    Path = path,
                    Existing = backed.TryGet(path, out var tags) ? tags : null,
                })
                .Select(file => new
                {
                    file.Path,
                    file.Existing,
                    NeedsKey = backKeys && NeedsAKey(file.Existing),
                })
                // Before reading any provenance, which costs a tag read per file.
                .Where(file =>
                    file.NeedsKey || (ownedOf is not null && CanTakeOwnedTags(file.Existing))
                )
                .Select(file => new
                {
                    file.Path,
                    file.Existing,
                    Keywords = KeywordsFor(ProvenanceOf(file.Path), ledger, ownedOf, file.NeedsKey),
                })
                .Where(file => file.Keywords.Count > 0)
                .Select(file => new
                {
                    file.Path,
                    file.Existing,
                    Keywords = Merged(file.Existing, file.Keywords),
                })
                // A write that changes nothing is not worth a reconcile — and one whose whole key
                // the user had taken off changes nothing.
                .Where(file =>
                    file.Existing is null
                    || !file
                        .Keywords.ToHashSet(StringComparer.Ordinal)
                        .SetEquals(file.Existing.Keywords)
                )
                .Select(file => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(root, file.Path),
                    Keywords = file.Keywords,
                }),
        ];
    }

    /// <summary>
    /// Whether this file's entry is missing a key that writing one would supply. Half a key is
    /// the audit's to report, not this to complete.
    /// </summary>
    internal static bool NeedsAKey(FileTags? existing) =>
        existing is null
        || (
            existing is { Key: null, HasPartialKey: false }
            // A keyword the writer does not know would be refused, taking the whole pass with it,
            // and dropping it to get the write through would lose what the user put there.
            && existing.Keywords.All(CratesideKeywords.IsKnown)
        );

    /// <summary>
    /// crateside's own tags go only onto an entry that already carries a key: the tagging stage's
    /// next write to a keyless entry would replace its bag, and them with it.
    /// </summary>
    internal static bool CanTakeOwnedTags(FileTags? existing) =>
        existing is { IsKeyTagged: true } && existing.Keywords.All(CratesideKeywords.IsKnown);

    /// <summary>
    /// The entry's whole bag plus the key it lacks and its own tags, each replacing the one of its
    /// group. The writer replaces the whole bag, so anything left out here is destroyed.
    /// </summary>
    internal static IReadOnlyList<string> Merged(FileTags? existing, IReadOnlyList<string> keywords)
    {
        var hidden = existing?.HiddenKeywords ?? [];
        var owned = keywords
            .Where(keyword => IsOwned(keyword) && !hidden.Contains(keyword, StringComparer.Ordinal))
            .ToArray();
        var replaced = owned.Select(GroupOf).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<string> kept = existing is null
            ? keywords.Where(keyword => !IsOwned(keyword)).ToArray()
            :
            [
                .. existing.Keywords.Where(keyword => !replaced.Contains(GroupOf(keyword))),
                .. keywords.Where(keyword =>
                    !hidden.Contains(keyword, StringComparer.Ordinal)
                    && (
                        keyword.StartsWith("Key|", StringComparison.Ordinal)
                        || (
                            existing.Type is null
                            && keyword.StartsWith("Type|", StringComparison.Ordinal)
                        )
                    )
                ),
            ];

        return [.. kept, .. owned];
    }

    private static bool IsOwned(string keyword) =>
        GenreVocabulary.IsGenreKeyword(keyword) || FamiliarityVocabulary.IsKeyword(keyword);

    private static string GroupOf(string keyword) => keyword.Split('|')[0];

    /// <summary>
    /// What one file's entry should gain, from its own provenance, the verdict the ledger settled
    /// and crateside's own tags. Empty for a file nothing can be said about.
    /// </summary>
    private static IReadOnlyList<string> KeywordsFor(
        StemProvenance? provenance,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf,
        bool needsKey
    )
    {
        if (provenance is not { SourceVideoId: { } videoId })
            return [];

        IReadOnlyList<string> key =
            needsKey && ledger.TryGetLatestByVideoId(videoId, out var run)
                ? TaggingRunner.KeywordsFor(run.Key, provenance.Preset, provenance.Type)
                : [];

        // An owned tag with no key would make a keyless entry, which is the tagging stage's to write.
        if (needsKey && key.Count is 0)
            return [];

        return [.. key, .. (ownedOf?.Invoke(videoId) ?? []).Where(CratesideKeywords.IsKnown)];
    }

    private StemProvenance? ProvenanceOf(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);

        if (_read.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Provenance;

        var provenance = StemTagReader.Read(path)?.Provenance;

        _read[path] = (stamp, provenance);

        return provenance;
    }
}
