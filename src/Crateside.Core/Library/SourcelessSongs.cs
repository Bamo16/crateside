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
    public static string KeyOf(string path) => $"{Prefix}{StemFileName.WithoutPreset(path)}";

    /// <summary>The library's StemForge-cut files that name no source, by <see cref="KeyOf"/>.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> Scan(
        string libraryFolder
    ) =>
        !Directory.Exists(libraryFolder)
            ? new Dictionary<string, IReadOnlyList<StemOnDisk>>()
            : Directory
                .EnumerateFiles(libraryFolder, "*", SearchOption.AllDirectories)
                .Where(SampleFiles.IsAudio)
                .Select(path => (Path: path, Provenance: StemTagReader.Read(path)?.Provenance))
                .Where(file =>
                    file.Provenance is { SourceVideoId: null, IsAdopted: false, Label: null }
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
