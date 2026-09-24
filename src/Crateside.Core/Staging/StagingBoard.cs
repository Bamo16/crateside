using Crateside.Core.Ledger;

namespace Crateside.Core.Staging;

/// <summary>
/// Builds the staging board by composing the readers. Purely derived — no state, no
/// done-flags, every call re-reads from disk.
/// </summary>
/// <remarks>
/// Reads the XMP rather than Ableton's index: this is the one root crateside writes, so the XMP
/// is ahead of the index there (ADR 0017).
/// </remarks>
public sealed class StagingBoard
{
    private readonly AbletonFolderInfoReader _folderInfoReader;

    public StagingBoard(AbletonFolderInfoReader? folderInfoReader = null) =>
        _folderInfoReader = folderInfoReader ?? new AbletonFolderInfoReader();

    /// <summary>The stem formats StemForge writes.</summary>
    internal static readonly string[] StemExtensions = [".flac", ".wav", ".aiff", ".mp3"];

    /// <summary>Whether a path is worth handing to the tag reader — TagLib throws on non-audio.</summary>
    internal static bool IsStemFile(string path) =>
        StemExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the staging folder and rolls its stems up into songs, newest-looking first.
    /// Returns an empty board — never throws — for a folder that does not exist.
    /// </summary>
    public IReadOnlyList<StagingSong> Build(string stagingRoot, KeyLedger ledger)
    {
        if (!Directory.Exists(stagingRoot))
            return [];

        var folderInfo = _folderInfoReader.Read(stagingRoot);

        var stems = Directory
            .EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(IsStemFile)
            .Select(path => new { Path = path, Tags = StemTagReader.Read(path) })
            .ToArray();

        return
        [
            .. stems
                // Falls back to the filename-derived key so an untagged stem still groups
                // with its siblings instead of becoming one row per file.
                .GroupBy(s => s.Tags?.SourceVideoId ?? SongKeyFromFileName(s.Path))
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

    private static StagingSong BuildSong(
        string groupKey,
        IReadOnlyList<(string Path, StemTags? Tags)> group,
        AbletonFolderInfo folderInfo,
        KeyLedger ledger
    )
    {
        var videoId = group.Select(g => g.Tags?.SourceVideoId).FirstOrDefault(id => id is not null);

        LedgerRun? verdict = null;
        if (videoId is not null)
            ledger.TryGetLatestByVideoId(videoId, out verdict);

        var stems = group
            .Select(g => BuildStem(g.Path, g.Tags, folderInfo, verdict))
            .OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StagingSong
        {
            SourceVideoId = videoId,
            DisplayName = DisplayName(group, verdict, groupKey),
            Verdict = verdict,
            Stems = stems,
        };
    }

    private static StagingStem BuildStem(
        string path,
        StemTags? tags,
        AbletonFolderInfo folderInfo,
        LedgerRun? verdict
    )
    {
        folderInfo.TryGet(path, out var abletonTags);

        var abletonKey = abletonTags?.Key;

        return new StagingStem
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
