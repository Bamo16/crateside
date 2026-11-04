using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>The files under some roots that share an upload, read once, for finding where a warp may come from.</summary>
public sealed class WarpSiblings
{
    /// <summary>Which cuts a tempo is read from, best first: an instrumental, then the kept source, then karaoke.</summary>
    private static readonly string?[] TempoKinds = ["Instrumental", null, "Karaoke"];

    private readonly ILookup<string, SiblingFile> _byTrackKey;

    private WarpSiblings(IEnumerable<SiblingFile> files) =>
        _byTrackKey = files.ToLookup(file => file.SourceTrackKey, StringComparer.Ordinal);

    public static WarpSiblings Scan(IEnumerable<string> roots) =>
        Of(
            roots
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateFiles)
                .Where(SampleFiles.IsAudio)
        );

    public static WarpSiblings Of(IEnumerable<string> audioPaths) =>
        new(audioPaths.Select(Read).OfType<SiblingFile>());

    /// <summary>Null when the file carries no upload to share, or was adopted rather than cut.</summary>
    public static SiblingFile? Read(string path) =>
        StemTagReader.Read(path)?.Provenance
            is { IsAdopted: false, SourceTrackKey: { } trackKey } provenance
        && (provenance.Preset ?? StemFileName.PresetOf(path)) is var preset
            ? new SiblingFile(path, trackKey, IsAuthority(preset), preset?.Split(' ')[0])
            : null;

    /// <summary>Every other file cut from the target's upload whose warp is the track's (#54).</summary>
    public IReadOnlyList<string> AuthoritiesFor(SiblingFile target) =>
        [
            .. _byTrackKey[target.SourceTrackKey]
                .Where(file =>
                    file.IsAuthority
                    && !string.Equals(file.Path, target.Path, StringComparison.OrdinalIgnoreCase)
                )
                .Select(file => file.Path),
        ];

    /// <summary>Every other cut of the target's upload of the same kind, such as an earlier instrumental of a re-cut one.</summary>
    public IReadOnlyList<string> SameKindAs(SiblingFile target) =>
        [
            .. _byTrackKey[target.SourceTrackKey]
                .Where(file =>
                    file.Kind == target.Kind
                    && !string.Equals(file.Path, target.Path, StringComparison.OrdinalIgnoreCase)
                )
                .Select(file => file.Path),
        ];

    /// <summary>Every cut of the target's upload under the scanned roots, the target included.</summary>
    public IEnumerable<SiblingFile> CutsOf(SiblingFile target) =>
        _byTrackKey[target.SourceTrackKey];

    /// <summary>The cuts a track's tempo can be read from, best first (#77).</summary>
    public static IEnumerable<SiblingFile> TempoSources(IEnumerable<SiblingFile> cuts) =>
        TempoKinds.SelectMany(kind => cuts.Where(cut => cut.IsAuthority && cut.Kind == kind));

    public IEnumerable<SiblingFile> In(string root) =>
        _byTrackKey
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
public sealed record SiblingFile(
    string Path,
    [property: JsonPropertyName("sourceVideoId")] string SourceTrackKey,
    bool IsAuthority,
    string? Kind
);
