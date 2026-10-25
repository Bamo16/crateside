using System.Collections.Concurrent;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Gives every unbacked file in a root the entry its verdict earns, so its key survives the next
/// write (ADR 0023), and every keyed entry crateside's own tags: genre (ADR 0030), familiarity (#52)
/// and its track's Collections (#130).
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
    /// <param name="corrections">Paths the user approved re-keying, each to the verdict they saw (ADR 0052).</param>
    /// <param name="index">Live's index of the root, which an imported file's Collections arrive in; null leaves Collections alone.</param>
    /// <param name="marksOf">A song's marks the maintainer set; none when left out.</param>
    /// <param name="beforeWrite">Called with the root just before a write that changes something.</param>
    /// <param name="song">Limits the pass to one song's files, as a mark's write is (#144).</param>
    public TagWriteOutcome Run(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf = null,
        bool backKeys = true,
        IReadOnlyDictionary<string, string>? corrections = null,
        FolderInfo? index = null,
        Func<string, IReadOnlyCollection<TrackMark>>? marksOf = null,
        Action<string>? beforeWrite = null,
        string? song = null
    )
    {
        IReadOnlyList<TagAssignment> unbacked;

        try
        {
            unbacked = Unbacked(root, ledger, ownedOf, backKeys, corrections, index, marksOf, song);
        }
        // A store Live is writing reads as nothing backed; wait for it rather than back everything.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TagWriteOutcome.Stale(ex.Message);
        }

        if (unbacked.Count > 0)
            beforeWrite?.Invoke(root);

        return CratesideKeywords.Apply(writer, root, unbacked);
    }

    /// <summary>
    /// Library files whose key, as the index holds it, is not their taggable verdict (ADR 0052).
    /// </summary>
    public IReadOnlyList<KeyDisagreement> Disagreements(
        string root,
        KeyLedger ledger,
        FolderInfo index
    ) =>
        Directory.Exists(root)
            ?
            [
                .. Directory
                    .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(SampleFiles.IsAudio)
                    .Select(path =>
                        index.TryGet(path, out var tags)
                        && tags.Key is { } applied
                        && ProvenanceOf(path) is { SourceVideoId: { } videoId }
                        && ledger.TryGetLatestByVideoId(videoId, out var run)
                        && run is { HasTaggableKey: true, Key: { } verdict }
                        && !verdict.Equals(applied, StringComparison.OrdinalIgnoreCase)
                            ? new KeyDisagreement(path, run.DisplayName, applied, verdict)
                            : null
                    )
                    .OfType<KeyDisagreement>(),
            ]
            : [];

    /// <summary>
    /// Every file under <paramref name="root"/> whose entry is missing a key, a tag or a Collection it
    /// could have, with what the entry should say; a key already in the store is never rewritten.
    /// </summary>
    /// <param name="index">Given, every keyed entry is put in its track's Collections and out of the warp states.</param>
    /// <param name="song">Given, only files of this song are looked at.</param>
    public IReadOnlyList<TagAssignment> Unbacked(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf = null,
        bool backKeys = true,
        IReadOnlyDictionary<string, string>? corrections = null,
        FolderInfo? index = null,
        Func<string, IReadOnlyCollection<TrackMark>>? marksOf = null,
        string? song = null
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
                    Approved = corrections?.GetValueOrDefault(path),
                })
                .Select(file => new
                {
                    file.Path,
                    file.Existing,
                    NeedsKey = backKeys && NeedsAKey(file.Existing),
                    CorrectTo = file.Approved is { } approved
                    && NeedsCorrection(file.Existing, approved)
                        ? approved
                        : null,
                })
                // Before reading any provenance, which costs a tag read per file.
                .Where(file =>
                    file.NeedsKey
                    || file.CorrectTo is not null
                    || (
                        (ownedOf is not null || index is not null)
                        && CanTakeOwnedTags(file.Existing)
                    )
                )
                .Where(file =>
                    song is null
                    || (
                        ProvenanceOf(file.Path) is { SourceVideoId: { } videoId }
                        && string.Equals(
                            ledger.SongFor(videoId) ?? videoId,
                            song,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                .Select(file =>
                {
                    var provenance = ProvenanceOf(file.Path);
                    var (keywords, corrects) = KeywordsFor(
                        provenance,
                        ledger,
                        ownedOf,
                        file.NeedsKey,
                        file.CorrectTo
                    );
                    var merged = WithMode(
                        Merged(file.Existing, keywords, correcting: corrects),
                        file.Existing,
                        provenance,
                        ledger,
                        keyWritten: file.NeedsKey || corrects
                    );
                    // A write that changes nothing is not worth a reconcile — and one whose whole
                    // key the user had taken off changes nothing.
                    var changes = file.Existing is null
                        ? keywords.Count > 0
                        : !merged
                            .ToHashSet(StringComparer.Ordinal)
                            .SetEquals(file.Existing.Keywords);
                    // An entry written without a key would make Live drop the one the index holds.
                    var keyed = keywords.Count > 0 || CanTakeOwnedTags(file.Existing);

                    return new
                    {
                        file.Path,
                        Keywords = changes ? merged : null,
                        Collections = index is not null && keyed
                            ? CollectionsFor(
                                file.Path,
                                file.Existing,
                                index,
                                provenance,
                                ledger,
                                marksOf
                            )
                            : null,
                    };
                })
                .Where(file => file.Keywords is not null || file.Collections is not null)
                .Select(file => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(root, file.Path),
                    Keywords = file.Keywords,
                    Collections = file.Collections,
                }),
        ];
    }

    /// <summary>
    /// The entry's Collections with its track's marks and no warp state, or null when it has them
    /// already. A file not in the store yet has its Collections in the index alone.
    /// </summary>
    private static IReadOnlyList<int>? CollectionsFor(
        string path,
        FileTags? existing,
        FolderInfo index,
        StemProvenance? provenance,
        KeyLedger ledger,
        Func<string, IReadOnlyCollection<TrackMark>>? marksOf
    )
    {
        LedgerRun? verdict = null;
        IReadOnlyCollection<TrackMark> mine = [];

        if (provenance is { SourceVideoId: { } videoId })
        {
            ledger.TryGetLatestByVideoId(videoId, out verdict);
            mine = marksOf?.Invoke(verdict?.Song ?? videoId) ?? [];
        }

        var has =
            existing?.Collections
            ?? (index.TryGet(path, out var indexed) ? indexed.Collections : []);
        var wanted = BrowserCollections.Merged(
            has,
            BrowserCollections.TrackMarksFor(verdict, mine),
            BrowserCollections.Cratesides
        );

        return existing is not null && wanted.SequenceEqual(existing.Collections) ? null : wanted;
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

    /// <summary>A keyed entry, all of whose keywords the writer knows, holding a key other than the approved one.</summary>
    internal static bool NeedsCorrection(FileTags? existing, string approved) =>
        CanTakeOwnedTags(existing)
        && !approved.Equals(existing!.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The entry's whole bag plus the key it lacks and its own tags, each replacing the one of its
    /// group. The writer replaces the whole bag, so anything left out here is destroyed.
    /// </summary>
    /// <param name="correcting">The key replaces the entry's own, approved by the user (ADR 0052).</param>
    internal static IReadOnlyList<string> Merged(
        FileTags? existing,
        IReadOnlyList<string> keywords,
        bool correcting = false
    )
    {
        var hidden = existing?.HiddenKeywords ?? [];
        var owned = keywords
            .Where(keyword => IsOwned(keyword) && !hidden.Contains(keyword, StringComparer.Ordinal))
            .ToArray();
        var replaced = owned.Select(GroupOf).ToHashSet(StringComparer.Ordinal);

        if (correcting)
            replaced.Add("Key");

        IReadOnlyList<string> kept = existing is null
            ? keywords.Where(keyword => !IsOwned(keyword)).ToArray()
            :
            [
                .. existing.Keywords.Where(keyword => !replaced.Contains(GroupOf(keyword))),
                .. keywords.Where(keyword =>
                    // An approved key is written whole, or the file would be left with half of one.
                    (correcting && IsKey(keyword))
                    || (
                        !hidden.Contains(keyword, StringComparer.Ordinal)
                        && (
                            IsKey(keyword)
                            || (
                                existing.Type is null
                                && keyword.StartsWith("Type|", StringComparison.Ordinal)
                            )
                        )
                    )
                ),
            ];

        return [.. kept, .. owned];
    }

    /// <summary>Gives a keyed entry the mode tag its verdict calls for and takes off any other (#127).</summary>
    private static IReadOnlyList<string> WithMode(
        IReadOnlyList<string> merged,
        FileTags? existing,
        StemProvenance? provenance,
        KeyLedger ledger,
        bool keyWritten
    )
    {
        if (
            provenance is not { SourceVideoId: { } videoId }
            || !ledger.TryGetLatestByVideoId(videoId, out var run)
            || run is not { HasTaggableKey: true, Key: { } verdict }
            || !verdict.Equals(
                keyWritten ? verdict : existing?.Key,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return merged;

        var wanted = ModeTags.KeywordFor(run.Center);
        var hidden = existing?.HiddenKeywords ?? [];

        return
        [
            .. merged.Where(keyword => !ModeTags.IsKeyword(keyword) || keyword == wanted),
            .. wanted is { } tag
            && !merged.Contains(tag)
            && !hidden.Contains(tag, StringComparer.Ordinal)
                ? [tag]
                : Array.Empty<string>(),
        ];
    }

    private static bool IsKey(string keyword) =>
        keyword.StartsWith("Key|", StringComparison.Ordinal);

    private static bool IsOwned(string keyword) =>
        GenreVocabulary.IsGenreKeyword(keyword) || FamiliarityVocabulary.IsKeyword(keyword);

    private static string GroupOf(string keyword) => keyword.Split('|')[0];

    /// <summary>
    /// What one file's entry should gain, from its own provenance, the verdict the ledger settled
    /// and crateside's own tags. Empty for a file nothing can be said about.
    /// </summary>
    /// <param name="correctTo">The verdict the user approved; ignored once the ledger has moved on from it.</param>
    private static (IReadOnlyList<string> Keywords, bool Corrects) KeywordsFor(
        StemProvenance? provenance,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf,
        bool needsKey,
        string? correctTo
    )
    {
        if (provenance is not { SourceVideoId: { } videoId })
            return ([], false);

        var hasRun = ledger.TryGetLatestByVideoId(videoId, out var run);
        var corrects =
            correctTo is not null
            && hasRun
            && run is { HasTaggableKey: true }
            && correctTo.Equals(run.Key, StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<string> key =
            (needsKey || corrects) && hasRun
                ? TaggingRunner.KeywordsFor(run!.Key, provenance.Preset, provenance.Type)
                : [];

        // An owned tag with no key would make a keyless entry, which is the tagging stage's to write.
        if (needsKey && key.Count is 0)
            return ([], false);

        return (
            [.. key, .. (ownedOf?.Invoke(videoId) ?? []).Where(CratesideKeywords.IsKnown)],
            corrects && key.Count > 0
        );
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

/// <summary>A library file whose key is not its verdict, as the user is shown it (ADR 0052).</summary>
public sealed record KeyDisagreement(string Path, string Song, string Applied, string Verdict);
