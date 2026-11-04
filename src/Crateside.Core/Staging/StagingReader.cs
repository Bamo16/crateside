using System.Collections.Concurrent;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Samples;

namespace Crateside.Core.Staging;

/// <summary>
/// Reads what is in Staging, by song, from disk on every call. Reads the XMP, which is ahead
/// of Ableton's index where crateside writes (ADR 0017).
/// </summary>
public sealed class StagingReader(LedgerStore store, FolderInfoReader? folderInfoReader = null)
{
    private readonly FolderInfoReader _folderInfoReader =
        folderInfoReader ?? new FolderInfoReader();

    private readonly ConcurrentDictionary<
        string,
        ((long Length, DateTime Written) Stamp, bool Has)
    > _defaultClips = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the staging folder and rolls its stems up into songs, newest-looking first.
    /// Returns nothing — never throws — for a folder that does not exist.
    /// </summary>
    /// <summary>Reads Staging where the paths say it is, against the ledger as it is now.</summary>
    public IReadOnlyList<StagedSong> Read(CratesidePaths paths) =>
        Build(paths.StagingFolder, store.Read());

    public IReadOnlyList<StagedSong> Build(string stagingRoot, KeyLedger ledger)
    {
        if (!Directory.Exists(stagingRoot))
            return [];

        var folderInfo = AbletonReads.TagsOrEmpty(_folderInfoReader.Read, stagingRoot);

        var stems = Directory
            .EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(SampleFiles.IsAudio)
            .Select(path => new { Path = path, Tags = StemTagReader.Read(path) })
            .ToArray();

        return
        [
            .. stems
                // Falls back to the filename-derived key so an untagged stem still groups
                // with its siblings instead of becoming one row per file.
                .GroupBy(s => s.Tags?.SourceTrackKey ?? SongKeyFromFileName(s.Path))
                .Select(group =>
                    BuildSong(
                        group.Key,
                        [.. group.Select(g => (g.Path, g.Tags))],
                        folderInfo,
                        ledger
                    )
                )
                .OrderBy(song => song.IsFullyTagged) // unfinished work first
                .ThenByDescending(song => song.AwaitingDetection)
                .ThenBy(song => song.DisplayName, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private StagedSong BuildSong(
        string groupKey,
        IReadOnlyList<(string Path, StemTags? Tags)> group,
        FolderInfo folderInfo,
        KeyLedger ledger
    )
    {
        var trackKey = group
            .Select(g => g.Tags?.SourceTrackKey)
            .FirstOrDefault(id => id is not null);

        LedgerRun? verdict = null;
        if (trackKey is not null)
            ledger.TryGetLatestByTrackKey(trackKey, out verdict);

        var stems = group
            .Select(g => BuildStem(g.Path, g.Tags, folderInfo, verdict))
            .OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StagedSong
        {
            SourceTrackKey = trackKey,
            DisplayName = DisplayName(group, verdict, groupKey),
            Verdict = verdict,
            Stems = stems,
        };
    }

    private bool HasDefaultClip(string path)
    {
        var sidecar = $"{path}.asd";

        if (new FileInfo(sidecar) is not { Exists: true } file)
            return false;

        var stamp = (file.Length, file.LastWriteTimeUtc);

        if (_defaultClips.TryGetValue(sidecar, out var kept) && kept.Stamp == stamp)
            return kept.Has;

        // A sidecar that could not be read is not remembered: Live may have it open.
        if (!AbletonReads.TryReadAnalysis(sidecar, out var analysis))
            return false;

        var has = analysis is { DefaultClip: not null };

        _defaultClips[sidecar] = (stamp, has);

        return has;
    }

    private StagedFile BuildStem(
        string path,
        StemTags? tags,
        FolderInfo folderInfo,
        LedgerRun? verdict
    )
    {
        folderInfo.TryGet(path, out var abletonTags);

        var abletonKey = abletonTags?.Key;

        return new StagedFile
        {
            Path = path,
            FileName = Path.GetFileName(path),
            Preset = tags?.Provenance?.Preset,
            IsPremium = tags?.Provenance?.IsPremium,
            AdoptedAs = tags?.Provenance is { IsAdopted: true, Label: { } adopted }
                ? adopted
                : null,
            AbletonKey = abletonKey,
            HasPartialKey = abletonTags?.HasPartialKey ?? false,
            HasDefaultClip = HasDefaultClip(path),
            KeyDisagreesWithVerdict =
                abletonKey is not null
                && verdict?.Key is { } verdictKey
                && !abletonKey.Equals(verdictKey, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// The best available name: the ledger identity, then the stem's own tags, then the
    /// filename with its preset suffix stripped.
    /// </summary>
    private static string DisplayName(
        IReadOnlyList<(string Path, StemTags? Tags)> group,
        LedgerRun? verdict,
        string groupKey
    )
    {
        if (verdict is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title })
            return $"{artist} - {title}";

        var tags = group.Select(g => g.Tags).FirstOrDefault(t => t?.Title is { Length: > 0 });

        if (tags is { Title: { Length: > 0 } tagTitle })
            return tags.Artist is { Length: > 0 } tagArtist
                ? $"{tagArtist} - {tagTitle}"
                : tagTitle;

        return groupKey;
    }

    private static string SongKeyFromFileName(string path) => StemFileName.WithoutPreset(path);
}
