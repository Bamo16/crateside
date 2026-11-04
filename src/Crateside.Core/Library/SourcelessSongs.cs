using Crateside.Core.Samples;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Library;

/// <summary>
/// Songs StemForge cut without recording a source (before about June 2026), keyed by their
/// file name since no video ID names them (#94).
/// </summary>
public static class SourcelessSongs
{
    private const string Prefix = "name:";

    /// <summary>Whether <paramref name="key"/> names a sourceless song; no video ID contains a colon.</summary>
    public static bool IsKey(string key) => key.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The identity <see cref="LibrarySong.Song"/> falls back to for this file.</summary>
    public static string KeyOf(string path) => KeyNamed(StemFileName.WithoutPreset(path));

    /// <summary>The key a song has once its files are named <paramref name="name"/>.</summary>
    public static string KeyNamed(string name) => $"{Prefix}{name}";

    public static IReadOnlyList<string> FilesOf(string libraryFolder, string key) =>
        Scan(libraryFolder).GetValueOrDefault(key)?.Select(file => file.Path).ToArray() ?? [];

    /// <summary>The library's StemForge-cut files that name no source, by <see cref="KeyOf"/>.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> Scan(
        string libraryFolder
    ) =>
        !Directory.Exists(libraryFolder)
            ? new Dictionary<string, IReadOnlyList<StemOnDisk>>()
            : new DirectoryInfo(libraryFolder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => SampleFiles.IsAudio(file.Name))
                .Select(file =>
                    (Path: file.FullName, Provenance: StemTagReader.Read(file)?.Provenance)
                )
                .Where(file =>
                    file.Provenance is { SourceTrackKey: null, IsAdopted: false, Label: null }
                )
                .GroupBy(file => KeyOf(file.Path), StringComparer.Ordinal)
                .ToDictionary(
                    song => song.Key,
                    song =>
                        (IReadOnlyList<StemOnDisk>)
                            [
                                .. song.Select(file => new StemOnDisk
                                {
                                    Path = file.Path,
                                    Root = libraryFolder,
                                    Preset = file.Provenance!.Preset,
                                    IsPremium = file.Provenance.IsPremium,
                                }),
                            ],
                    StringComparer.Ordinal
                );
}
