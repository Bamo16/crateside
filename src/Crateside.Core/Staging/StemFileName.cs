using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Crateside.Core.Staging;

/// <summary>How a track's files are named: the track's name, then the preset in parentheses.</summary>
public static partial class StemFileName
{
    /// <summary>The same file under a different track name, keeping its preset and extension.</summary>
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
    /// Appended to the sample's <em>whole</em> name, extension included:
    /// <c>99th Street (Drums).flac.asd</c>, not <c>99th Street (Drums).asd</c>.
    /// </remarks>
    public static string AnalysisFor(string samplePath) => samplePath + ".asd";

    /// <summary>The name with its preset stripped — what the files of one song have in common.</summary>
    public static string WithoutPreset(string path) =>
        PresetSuffix.Replace(Path.GetFileNameWithoutExtension(path), string.Empty).Trim();

    /// <summary>Whether a name can be put on a file as it stands.</summary>
    public static bool IsUsable([NotNullWhen(true)] string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex PresetSuffix { get; }
}
