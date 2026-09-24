using System.Collections.Concurrent;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Gives every unbacked file in a root the entry its verdict earns, so its key survives the next
/// write (ADR 0023), and every keyed entry its settled genre (ADR 0030).
/// </summary>
public sealed class BackingRunner(
    AbletonFolderInfoReader folderInfo,
    AbletonFolderInfoWriter writer
)
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
    /// <param name="genreOf">A video ID's settled genre, or null to leave its entry's genre alone.</param>
    /// <param name="backKeys">False where another stage writes the keys, which is staging.</param>
    public TagWriteOutcome Run(
        string root,
        KeyLedger ledger,
        Func<string, string?>? genreOf = null,
        bool backKeys = true
    ) => writer.Apply(root, Unbacked(root, ledger, genreOf, backKeys));

    /// <summary>
    /// Every file under <paramref name="root"/> whose entry is missing a key or a genre it could
    /// have, with what the entry should say; a key already in the store is never rewritten.
    /// </summary>
    public IReadOnlyList<TagAssignment> Unbacked(
        string root,
        KeyLedger ledger,
        Func<string, string?>? genreOf = null,
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
                .Where(StagingBoard.IsStemFile)
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
                    file.NeedsKey || (genreOf is not null && CanTakeAGenre(file.Existing))
                )
                .Select(file => new
                {
                    file.Path,
                    file.Existing,
                    Keywords = KeywordsFor(ProvenanceOf(file.Path), ledger, genreOf, file.NeedsKey),
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
    internal static bool NeedsAKey(AbletonFileTags? existing) =>
        existing is null
        || (
            existing is { Key: null, HasPartialKey: false }
            // A keyword the writer does not know would be refused, taking the whole pass with it,
            // and dropping it to get the write through would lose what the user put there.
            && existing.Keywords.All(AbletonFolderInfoWriter.IsValidKeyword)
        );

    /// <summary>
    /// A genre goes only onto an entry that already carries a key: the tagging stage's next write
    /// to a keyless entry would replace its bag, genre and all.
    /// </summary>
    internal static bool CanTakeAGenre(AbletonFileTags? existing) =>
        existing is { IsKeyTagged: true }
        && existing.Keywords.All(AbletonFolderInfoWriter.IsValidKeyword);

    /// <summary>
    /// The entry's whole bag plus the key it lacks and its settled genre. The writer replaces the
    /// whole bag, so anything left out here is destroyed.
    /// </summary>
    internal static IReadOnlyList<string> Merged(
        AbletonFileTags? existing,
        IReadOnlyList<string> keywords
    )
    {
        var hidden = existing?.HiddenKeywords ?? [];
        var genre =
            keywords.FirstOrDefault(GenreVocabulary.IsGenreKeyword) is { } wanted
            && !hidden.Contains(wanted, StringComparer.Ordinal)
                ? wanted
                : null;

        IReadOnlyList<string> kept = existing is null
            ? keywords.Where(keyword => !GenreVocabulary.IsGenreKeyword(keyword)).ToArray()
            :
            [
                .. existing.Keywords.Where(keyword =>
                    genre is null || !GenreVocabulary.IsGenreKeyword(keyword)
                ),
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

        return genre is null ? kept : [.. kept, genre];
    }

    /// <summary>
    /// What one file's entry should gain, from its own provenance, the verdict the ledger settled
    /// and the genre the user settled. Empty for a file nothing can be said about.
    /// </summary>
    private static IReadOnlyList<string> KeywordsFor(
        StemProvenance? provenance,
        KeyLedger ledger,
        Func<string, string?>? genreOf,
        bool needsKey
    )
    {
        if (provenance is not { SourceVideoId: { } videoId })
            return [];

        IReadOnlyList<string> key =
            needsKey && ledger.TryGetLatestByVideoId(videoId, out var run)
                ? TaggingRunner.KeywordsFor(run.Key, provenance.Preset, provenance.Type)
                : [];

        // A genre with no key would make a keyless entry, which is the tagging stage's to write.
        if (needsKey && key.Count is 0)
            return [];

        return genreOf?.Invoke(videoId) is { } genre && GenreVocabulary.IsGenre(genre)
            ? [.. key, GenreVocabulary.Keyword(genre)]
            : key;
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
