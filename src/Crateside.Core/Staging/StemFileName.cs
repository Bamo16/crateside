using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Crateside.Core.Staging;

/// <summary>
/// How a track's files are named: the track's name, then the preset in parentheses.
/// </summary>
/// <remarks>
/// StemForge's rule, read back rather than reimplemented — <c>Artist - Title (Vocal - Full).flac</c>
/// — and the one place renaming and grouping both decide where the name ends.
/// </remarks>
public static partial class StemFileName
{
    /// <summary>The same file under a different track name, keeping its preset and extension.</summary>
    /// <remarks>
    /// The preset comes from the existing name rather than from the file's provenance, so this
    /// needs no tag read and leaves a file the user has already renamed by hand alone about it.
    /// </remarks>
    public static string Renamed(string path, string name) =>
        Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            name
                + PresetSuffix.Match(Path.GetFileNameWithoutExtension(path)).Value
                + Path.GetExtension(path)
        );

    /// <summary>
    /// Ableton's analysis file for a sample, which is where the warp markers live.
    /// </summary>
    /// <remarks>
    /// The sample's <em>whole</em> name plus <c>.asd</c>, extension included — the real folder
    /// holds <c>Starjunk 95 - 99th Street (Drums).flac.asd</c>. So it does not survive a rename on
    /// its own: Ableton finds no sidecar under the new name and analyses the file again from
    /// scratch, losing the markers and leaving the old one behind pointing at nothing.
    /// </remarks>
    public static string AnalysisFor(string samplePath) => samplePath + ".asd";

    /// <summary>The name with its preset stripped — what the files of one song have in common.</summary>
    public static string WithoutPreset(string path) =>
        PresetSuffix.Replace(Path.GetFileNameWithoutExtension(path), string.Empty).Trim();

    /// <summary>
    /// Whether a name can be put on a file as it stands.
    /// </summary>
    /// <remarks>
    /// <see cref="string.IsNullOrWhiteSpace"/> rather than a pattern, because a name of spaces
    /// passes every length check and then names a file nobody can find.
    /// </remarks>
    public static bool IsUsable([NotNullWhen(true)] string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex PresetSuffix { get; }
}
