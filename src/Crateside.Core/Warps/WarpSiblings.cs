using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>The files under some roots that share an upload, read once, for finding where a warp may come from.</summary>
public sealed class WarpSiblings
{
    private readonly ILookup<string, SiblingFile> _byVideoId;

    private WarpSiblings(IEnumerable<SiblingFile> files) =>
        _byVideoId = files.ToLookup(file => file.SourceVideoId, StringComparer.Ordinal);

    public static WarpSiblings Scan(IEnumerable<string> roots) =>
        new(
            roots
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateFiles)
                .Where(SampleFiles.IsAudio)
                .Select(Read)
                .OfType<SiblingFile>()
        );

    /// <summary>Null when the file carries no upload to share, or was adopted rather than cut.</summary>
    public static SiblingFile? Read(string path) =>
        StemTagReader.Read(path)?.Provenance
            is { IsAdopted: false, SourceVideoId: { } videoId } provenance
            ? new SiblingFile(path, videoId, IsAuthority(path, provenance))
            : null;

    /// <summary>Every other file cut from the target's upload whose warp is the track's (#54).</summary>
    public IReadOnlyList<string> AuthoritiesFor(SiblingFile target) =>
        [
            .. _byVideoId[target.SourceVideoId]
                .Where(file =>
                    file.IsAuthority
                    && !string.Equals(file.Path, target.Path, StringComparison.OrdinalIgnoreCase)
                )
                .Select(file => file.Path),
        ];

    public IEnumerable<SiblingFile> In(string root) =>
        _byVideoId
            .SelectMany(group => group)
            .Where(file =>
                string.Equals(
                    Path.GetDirectoryName(file.Path),
                    Path.TrimEndingDirectorySeparator(root),
                    StringComparison.OrdinalIgnoreCase
                )
            );

    /// <summary>
    /// An instrumental, which the user warps the track by, or the kept source; a vocal may have been
    /// re-warped on its own. A kept source is known only by a name with no parenthetical.
    /// </summary>
    private static bool IsAuthority(string path, StemProvenance provenance) =>
        (provenance.Preset ?? StemFileName.PresetOf(path)) switch
        {
            null => true,
            var preset => preset.StartsWith("Instrumental", StringComparison.Ordinal)
                || preset is "Karaoke",
        };
}

public sealed record SiblingFile(string Path, string SourceVideoId, bool IsAuthority);
