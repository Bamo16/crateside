using System.Text.RegularExpressions;
using Crateside.Core.Ledger;

namespace Crateside.Core.Staging;

/// <summary>
/// Builds the staging board by composing the readers. Purely derived: it holds no state, has
/// no done-flags, and every call re-reads from disk.
/// </summary>
public sealed partial class StagingBoard
{
    private readonly StemTagReader _tagReader;
    private readonly AbletonFolderInfoReader _folderInfoReader;

    public StagingBoard(
        StemTagReader? tagReader = null,
        AbletonFolderInfoReader? folderInfoReader = null
    )
    {
        _tagReader = tagReader ?? new StemTagReader();
        _folderInfoReader = folderInfoReader ?? new AbletonFolderInfoReader();
    }

    /// <summary>The stem formats StemForge writes.</summary>
    internal static readonly string[] StemExtensions = [".flac", ".wav", ".aiff", ".mp3"];

    /// <summary>
    /// Whether a path is worth handing to the tag reader. Checking first matters: both stem
    /// roots are roughly half <c>.asd</c> sidecars, and asking TagLib to open one throws — so
    /// skipping them avoids hundreds of exceptions per scan, which is the expensive way to
    /// discover a file is not audio.
    /// </summary>
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
            .Select(path => new { Path = path, Tags = _tagReader.Read(path) })
            .ToArray();

        return
        [
            .. stems
                // A stem with no video ID still belongs on the board — it is precisely the
                // one the user needs to know about. Fall back to the filename so it groups
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
            AbletonKey = abletonKey,
            HasPartialKey = abletonTags?.HasPartialKey ?? false,
            KeyDisagreesWithVerdict =
                abletonKey is not null
                && verdict?.Key is { } verdictKey
                && !abletonKey.Equals(verdictKey, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Names the row from the best source available: the ledger identity, then the stem's own
    /// tags, then the filename with its preset suffix stripped.
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

    /// <summary>
    /// Groups stems of one song when provenance is missing, by stripping the trailing preset
    /// parenthetical StemForge writes — "Artist - Title (Vocal - Full).flac".
    /// </summary>
    private static string SongKeyFromFileName(string path) =>
        PresetSuffix().Replace(Path.GetFileNameWithoutExtension(path), string.Empty).Trim();

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex PresetSuffix();
}
