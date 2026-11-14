using System.Collections.Concurrent;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;
using Crateside.Core.Vocals;
using Microsoft.Extensions.Logging;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Gives every unbacked file in a root the entry its verdict earns, so its key survives the next
/// write (ADR 0023), and every keyed entry crateside's own tags: genres (ADR 0088), familiarity (#52)
/// and its track's Collections (#130). A key that differs from its song's verdict is rewritten to it
/// (ADR 0052, amended by #175).
/// </summary>
public sealed class BackingRunner(
    FolderInfoReader folderInfo,
    FolderInfoWriter writer,
    CollectionSettingsStore collections,
    ILogger<BackingRunner> log
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
    /// <param name="ownedOf">A video ID's keywords in crateside's own groups; a group left out is left alone.</param>
    /// <param name="retiredOf">A video ID's keywords crateside wrote before and no longer wants, which are taken off; every other keyword is left alone.</param>
    /// <param name="backKeys">False where another stage writes the keys, which is staging.</param>
    /// <param name="correctKeys">True rewrites a keyed entry's key to its song's verdict where they differ (#175).</param>
    /// <param name="index">Live's index of the root, which an imported file's Collections arrive in; null leaves Collections alone.</param>
    /// <param name="marksOf">A song's marks the maintainer set; none when left out.</param>
    /// <param name="beforeWrite">Called with the root just before a write that changes something.</param>
    /// <param name="song">Limits the pass to one song's files, as a mark's write is (#144).</param>
    public TagWriteOutcome Run(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf = null,
        bool backKeys = true,
        bool correctKeys = true,
        FolderInfo? index = null,
        Func<string, IReadOnlyCollection<TrackMark>>? marksOf = null,
        Action<string>? beforeWrite = null,
        string? song = null,
        Func<string, IReadOnlyList<string>>? retiredOf = null
    )
    {
        IReadOnlyList<Planned> unbacked;

        try
        {
            unbacked = Plan(
                root,
                ledger,
                ownedOf,
                backKeys,
                correctKeys,
                index,
                marksOf,
                song,
                retiredOf
            );
        }
        // A store Live is writing reads as nothing backed; wait for it rather than back everything.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TagWriteOutcome.Stale(ex.Message);
        }

        if (unbacked.Count > 0)
            beforeWrite?.Invoke(root);

        var outcome = CratesideKeywords.Apply(
            writer,
            root,
            [.. unbacked.Select(planned => planned.Assignment)]
        );

        if (outcome is TagWriteOutcome.Written)
            foreach (var change in unbacked.Select(planned => planned.Retag).OfType<Retag>())
                log.LogInformation(
                    "retagged {File}: {Old} → {New}",
                    Path.GetFileName(change.Path),
                    change.Old,
                    change.New
                );

        return outcome;
    }

    /// <summary>
    /// Every file under <paramref name="root"/> whose entry is missing a key, a tag or a Collection it
    /// could have, with what the entry should say.
    /// </summary>
    /// <param name="index">Given, every keyed entry is put in its track's Collections and out of the warp states.</param>
    /// <param name="song">Given, only files of this song are looked at.</param>
    public IReadOnlyList<TagAssignment> Unbacked(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf = null,
        bool backKeys = true,
        bool correctKeys = true,
        FolderInfo? index = null,
        Func<string, IReadOnlyCollection<TrackMark>>? marksOf = null,
        string? song = null,
        Func<string, IReadOnlyList<string>>? retiredOf = null
    ) =>
        [
            .. Plan(root, ledger, ownedOf, backKeys, correctKeys, index, marksOf, song, retiredOf)
                .Select(planned => planned.Assignment),
        ];

    /// <summary>
    /// The descriptive keywords the person added in Live, or took off one crateside wrote, on the
    /// files of a keyed track (ADR 0087). An unreadable store reads as no edits; the next pass reads it again.
    /// </summary>
    /// <param name="index">Live's index of the root, which holds an addition before the store does; null reads the store alone.</param>
    /// <param name="writtenOf">A song's keywords crateside has written; an owned keyword outside it is the person's.</param>
    public IReadOnlyList<LiveEdit> LiveEdits(
        string root,
        KeyLedger ledger,
        FolderInfo? index,
        Func<string, IReadOnlySet<string>> writtenOf
    )
    {
        if (!Directory.Exists(root))
            return [];

        FolderInfo backed;

        try
        {
            backed = folderInfo.Read(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var edits = new List<LiveEdit>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!SampleFiles.IsAudio(path))
                continue;

            var stored = backed.TryGet(path, out var held) ? held : null;
            var indexed = index is not null && index.TryGet(path, out var seen) ? seen : null;
            var present = (stored?.Keywords ?? []).Concat(indexed?.Keywords ?? []).Distinct();
            var hidden = stored?.HiddenKeywords ?? [];

            if (!present.Concat(hidden).Any(IsDescriptive))
                continue;

            if (ProvenanceOf(path) is not { SourceTrackKey: { } trackKey } provenance)
                continue;

            var song = ledger.SongFor(trackKey) ?? trackKey;
            var stem = $"{trackKey}|{provenance.Preset ?? provenance.Model ?? provenance.Label}";
            var written = writtenOf(song);

            edits.AddRange(
                present
                    .Where(keyword =>
                        IsDescriptive(keyword)
                        && (VocalVocabulary.IsKeyword(keyword) || !written.Contains(keyword))
                    )
                    .Select(keyword => new LiveEdit(song, stem, keyword, Removed: false))
            );
            edits.AddRange(
                hidden
                    .Where(keyword =>
                        IsDescriptive(keyword)
                        && (VocalVocabulary.IsKeyword(keyword) || written.Contains(keyword))
                    )
                    .Select(keyword => new LiveEdit(song, stem, keyword, Removed: true))
            );
        }

        return edits;
    }

    /// <summary>
    /// The Meter keywords the person set in Live on the files of a keyed track, and the ones they
    /// took off (#154). An unreadable store reads as none; the next pass reads it again.
    /// </summary>
    /// <param name="index">Live's index of the root, which holds an addition before the store does; null reads the store alone.</param>
    public IReadOnlyList<StemMeter> MeterReadings(string root, KeyLedger ledger, FolderInfo? index)
    {
        if (!Directory.Exists(root))
            return [];

        FolderInfo backed;

        try
        {
            backed = folderInfo.Read(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<StemMeter>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!SampleFiles.IsAudio(path))
                continue;

            var stored = backed.TryGet(path, out var held) ? held : null;
            var indexed = index is not null && index.TryGet(path, out var seen) ? seen : null;
            var present = Meters((stored?.Keywords ?? []).Concat(indexed?.Keywords ?? []));
            var removed = Meters(stored?.HiddenKeywords ?? []);

            if (
                present.Count + removed.Count is 0
                || ProvenanceOf(path) is not { SourceTrackKey: { } trackKey } provenance
            )
                continue;

            found.Add(
                new StemMeter(
                    ledger.SongFor(trackKey) ?? trackKey,
                    $"{trackKey}|{provenance.Preset ?? provenance.Model ?? provenance.Label}",
                    present.Except(removed).ToHashSet(StringComparer.Ordinal),
                    removed
                )
            );
        }

        return found;
    }

    private static HashSet<string> Meters(IEnumerable<string> keywords) =>
        [.. keywords.Select(MeterTags.ValueOf).OfType<string>()];

    private static bool IsDescriptive(string keyword) =>
        IsOwned(keyword) || VocalVocabulary.IsKeyword(keyword);

    private IReadOnlyList<Planned> Plan(
        string root,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf,
        bool backKeys,
        bool correctKeys,
        FolderInfo? index,
        Func<string, IReadOnlyCollection<TrackMark>>? marksOf,
        string? song,
        Func<string, IReadOnlyList<string>>? retiredOf
    )
    {
        if (!Directory.Exists(root))
            return [];

        var backed = folderInfo.Read(root);
        var settings = index is null ? new CollectionSettings() : collections.Read();

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
                    MayCorrect = correctKeys && CanTakeOwnedTags(file.Existing),
                })
                // Before reading any provenance, which costs a tag read per file.
                .Where(file =>
                    file.NeedsKey
                    || file.MayCorrect
                    || (
                        (ownedOf is not null || index is not null)
                        && CanTakeOwnedTags(file.Existing)
                    )
                )
                .Where(file =>
                    song is null
                    || (
                        ProvenanceOf(file.Path) is { SourceTrackKey: { } trackKey }
                        && string.Equals(
                            ledger.SongFor(trackKey) ?? trackKey,
                            song,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                .Select(file =>
                {
                    var provenance = ProvenanceOf(file.Path);
                    var (keywords, correctedTo) = KeywordsFor(
                        provenance,
                        ledger,
                        ownedOf,
                        file.NeedsKey,
                        file.MayCorrect ? file.Existing!.Key : null
                    );
                    var corrects = correctedTo is not null;
                    var merged = WithMode(
                        Merged(
                            file.Existing,
                            keywords,
                            correcting: corrects,
                            retired: provenance is { SourceTrackKey: { } trackKey }
                                ? retiredOf?.Invoke(trackKey)
                                : null
                        ),
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
                        Retag = correctedTo is null
                            ? null
                            : new Retag(file.Path, file.Existing!.Key!, correctedTo),
                        Keywords = changes ? merged : null,
                        Collections = index is not null && keyed
                            ? CollectionsFor(
                                settings,
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
                .Select(file => new Planned(
                    new TagAssignment
                    {
                        RelativePath = Path.GetRelativePath(root, file.Path),
                        Keywords = file.Keywords,
                        Collections = file.Collections,
                    },
                    file.Retag
                )),
        ];
    }

    /// <summary>
    /// The entry's Collections with its track's marks and no warp state, or null when it has them
    /// already. A file not in the store yet has its Collections in the index alone.
    /// </summary>
    private static IReadOnlyList<int>? CollectionsFor(
        CollectionSettings settings,
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

        if (provenance is { SourceTrackKey: { } trackKey })
        {
            ledger.TryGetLatestByTrackKey(trackKey, out verdict);
            mine = marksOf?.Invoke(verdict?.Song ?? trackKey) ?? [];
        }

        var has =
            existing?.Collections
            ?? (index.TryGet(path, out var indexed) ? indexed.Collections : []);
        var wanted = settings.Merged(
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
            && existing.Keywords.All(IsKnownOrRetired)
        );

    /// <summary>
    /// crateside's own tags go only onto an entry that already carries a key: the tagging stage's
    /// next write to a keyless entry would replace its bag, and them with it.
    /// </summary>
    internal static bool CanTakeOwnedTags(FileTags? existing) =>
        existing is { IsKeyTagged: true } && existing.Keywords.All(IsKnownOrRetired);

    // A retired Genre| keyword must not keep a file from being rewritten: the rewrite takes it off.
    private static bool IsKnownOrRetired(string keyword) =>
        CratesideKeywords.IsKnown(keyword) || GenreVocabulary.IsRetiredKeyword(keyword);

    /// <summary>
    /// The entry's whole bag plus the key it lacks and its own tags, each replacing the one of its
    /// group, except <c>Genres</c>, which only gains: Live's own content carries tags of that group
    /// too. The writer replaces the whole bag, so anything left out here is destroyed.
    /// </summary>
    /// <param name="correcting">The key replaces the entry's own, which differs from the verdict (ADR 0052).</param>
    internal static IReadOnlyList<string> Merged(
        FileTags? existing,
        IReadOnlyList<string> keywords,
        bool correcting = false,
        IReadOnlyList<string>? retired = null
    )
    {
        var hidden = existing?.HiddenKeywords ?? [];
        var owned = keywords
            .Where(keyword =>
                (IsOwned(keyword) || IsMeter(keyword))
                && !hidden.Contains(keyword, StringComparer.Ordinal)
            )
            .ToArray();
        var replaced = owned
            .Select(GroupOf)
            .Where(group => group != GenreVocabulary.Group)
            .ToHashSet(StringComparer.Ordinal);

        if (correcting)
            replaced.Add("Key");

        IReadOnlyList<string> kept = existing is null
            ? keywords.Where(keyword => !IsOwned(keyword) && !IsMeter(keyword)).ToArray()
            :
            [
                .. existing.Keywords.Where(keyword =>
                    !replaced.Contains(GroupOf(keyword))
                    && !GenreVocabulary.IsRetiredKeyword(keyword)
                    && retired?.Contains(keyword, StringComparer.Ordinal) is not true
                ),
                .. keywords.Where(keyword =>
                    // A corrected key is written whole, or the file would be left with half of one.
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

        return [.. kept.Concat(owned).Distinct(StringComparer.Ordinal)];
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
            provenance is not { SourceTrackKey: { } trackKey }
            || !ledger.TryGetLatestByTrackKey(trackKey, out var run)
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

    private static bool IsMeter(string keyword) =>
        keyword.StartsWith($"{MeterTags.Group}|", StringComparison.Ordinal);

    private static bool IsOwned(string keyword) =>
        GenreVocabulary.IsGenreKeyword(keyword) || FamiliarityVocabulary.IsKeyword(keyword);

    private static string GroupOf(string keyword) => keyword.Split('|')[0];

    /// <summary>
    /// What one file's entry should gain, from its own provenance, the verdict the ledger settled
    /// and crateside's own tags. Empty for a file nothing can be said about.
    /// </summary>
    /// <param name="keyToCorrect">A key the entry holds that the verdict should replace; null leaves it be.</param>
    /// <returns>The keywords to write, and the verdict a key is corrected to, or null where none is.</returns>
    private static (IReadOnlyList<string> Keywords, string? CorrectedTo) KeywordsFor(
        StemProvenance? provenance,
        KeyLedger ledger,
        Func<string, IReadOnlyList<string>>? ownedOf,
        bool needsKey,
        string? keyToCorrect
    )
    {
        if (provenance is not { SourceTrackKey: { } trackKey })
            return ([], null);

        var hasRun = ledger.TryGetLatestByTrackKey(trackKey, out var run);
        var corrects =
            keyToCorrect is not null
            && hasRun
            && run is { HasTaggableKey: true, Key: { } verdict }
            && !verdict.Equals(keyToCorrect, StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<string> key =
            (needsKey || corrects) && hasRun
                ? TaggingRunner.KeywordsFor(run!.Key, provenance.Preset, provenance.Type)
                : [];

        // An owned tag with no key would make a keyless entry, which is the tagging stage's to write.
        if (needsKey && key.Count is 0)
            return ([], null);

        return (
            [.. key, .. (ownedOf?.Invoke(trackKey) ?? []).Where(CratesideKeywords.IsKnown)],
            corrects && key.Count > 0 ? run!.Key : null
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

internal sealed record Retag(string Path, string Old, string New);

internal sealed record Planned(TagAssignment Assignment, Retag? Retag);
