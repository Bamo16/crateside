using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>The files under some roots that share an upload, read once, for finding where a warp may come from.</summary>
public sealed class WarpSiblings
{
    /// <summary>Which cuts a tempo is read from, best first: an instrumental, then the kept source, then karaoke.</summary>
    private static readonly string?[] TempoKinds = ["Instrumental", null, "Karaoke"];

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
        && (provenance.Preset ?? StemFileName.PresetOf(path)) is var preset
            ? new SiblingFile(path, videoId, IsAuthority(preset), preset?.Split(' ')[0])
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

    /// <summary>Every other cut of the target's upload of the same kind, such as an earlier instrumental of a re-cut one.</summary>
    public IReadOnlyList<string> SameKindAs(SiblingFile target) =>
        [
            .. _byVideoId[target.SourceVideoId]
                .Where(file =>
                    file.Kind == target.Kind
                    && !string.Equals(file.Path, target.Path, StringComparison.OrdinalIgnoreCase)
                )
                .Select(file => file.Path),
        ];

    /// <summary>Every cut of the target's upload under the scanned roots, the target included.</summary>
    public IEnumerable<SiblingFile> CutsOf(SiblingFile target) => _byVideoId[target.SourceVideoId];

    /// <summary>The cuts a track's tempo can be read from, best first (#77).</summary>
    public static IEnumerable<SiblingFile> TempoSources(IEnumerable<SiblingFile> cuts) =>
        TempoKinds.SelectMany(kind => cuts.Where(cut => cut.IsAuthority && cut.Kind == kind));

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
    private static bool IsAuthority(string? preset) =>
        preset switch
        {
            null => true,
            _ => preset.StartsWith("Instrumental", StringComparison.Ordinal) || preset is "Karaoke",
        };
}

/// <param name="Kind">The preset's first word (<c>Instrumental</c>, <c>Vocal</c>), or null for the kept source.</param>
public sealed record SiblingFile(string Path, string SourceVideoId, bool IsAuthority, string? Kind);
