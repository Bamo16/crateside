using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Crateside.Core.Samples;

/// <summary>How a track's files are named: the track's name, then the preset in parentheses.</summary>
public static partial class StemFileName
{
    /// <summary>
    /// The same file under a different track name, keeping its preset and extension. A name too
    /// long for the folder it lands in is cut by <see cref="FileTitle"/>.
    /// </summary>
    public static string Renamed(string path, string name, string? destination = null)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;

        return Path.Combine(
            destination ?? directory,
            FileTitle.Fit(
                name,
                FileTitle.MaxLengthIn(destination ?? directory, FileTitle.LongPathsEnabled)
            )
                + PresetSuffix.Match(Path.GetFileNameWithoutExtension(path)).Value
                + Path.GetExtension(path)
        );
    }

    /// <summary>
    /// The file's name with <c> (label)</c> added before its extension, in a name that still fits.
    /// </summary>
    public static string Labeled(string path, string label)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var title = FileTitle.Fit(
            Path.GetFileNameWithoutExtension(path),
            FileTitle.MaxLengthIn(directory, FileTitle.LongPathsEnabled)
        );

        return Path.Combine(directory, $"{title} ({label}){Path.GetExtension(path)}");
    }

    /// <summary>
    /// Ableton's analysis file for a sample, where the warp markers live — the whole name plus
    /// <c>.asd</c>, as in <c>99th Street (Drums).flac.asd</c>.
    /// </summary>
    public static string AnalysisFor(string samplePath) => samplePath + ".asd";

    /// <summary>The name with its preset stripped — what the files of one song have in common.</summary>
    public static string WithoutPreset(string path) =>
        PresetSuffix.Replace(Path.GetFileNameWithoutExtension(path), string.Empty).Trim();

    /// <summary>
    /// What the name claims the preset is, or null where it claims nothing. Only the separator's
    /// catalog can confirm the claim.
    /// </summary>
    public static string? PresetOf(string path) =>
        PresetSuffix.Match(Path.GetFileNameWithoutExtension(path)) is { Success: true } match
            ? match.Groups["preset"].Value
            : null;

    /// <summary>Whether a name can be put on a file as it stands.</summary>
    public static bool IsUsable([NotNullWhen(true)] string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>The name with every character a file name cannot hold replaced by an underscore.</summary>
    public static string FileSafe(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [GeneratedRegex(@"\s*\((?<preset>[^()]*)\)\s*$")]
    private static partial Regex PresetSuffix { get; }
}
