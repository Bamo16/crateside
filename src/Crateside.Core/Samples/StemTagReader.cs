using System.Collections.Concurrent;

namespace Crateside.Core.Samples;

/// <summary>
/// Reads a stem's tags off disk with TagLibSharp, and again only when the file's size or modified
/// time moves.
/// </summary>
public static class StemTagReader
{
    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.Ordinal);

    /// <summary>The audio files under <paramref name="root"/> whose tags name this upload.</summary>
    public static IReadOnlyList<string> FilesCutFrom(string root, string videoId) =>
        Directory.Exists(root)
            ?
            [
                .. Directory
                    .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(SampleFiles.IsAudio)
                    .Where(path => Read(path)?.Provenance?.SourceVideoId == videoId),
            ]
            : [];

    /// <summary>The tags crateside needs from a stem file; null if it is unreadable or unsupported.</summary>
    public static StemTags? Read(string path)
    {
        if (new FileInfo(path) is not { Exists: true } file)
            return null;

        var stamp = (file.Length, file.LastWriteTimeUtc);

        if (Cache.TryGetValue(path, out var entry) && entry.Stamp == stamp)
            return entry.Tags;

        // A failed read is not kept: a file mid-write fails once and reads when it is done.
        if (ReadFromDisk(path) is not { } tags)
            return null;

        Cache[path] = new Entry(stamp, tags);

        return tags;
    }

    /// <summary>The preset the stem was cut with: its provenance's, else its file name's.</summary>
    // A kept source's name can hold a parenthetical that is not a preset, so an older stem's comment
    // that names none yields to its file name only where the name says Vocal or Instrumental.
    public static string? PresetOf(string path) =>
        Read(path)?.Provenance is { } provenance
            ? provenance.Preset
                ?? (
                    StemFileName.PresetOf(path) is { } named
                    && (
                        named.StartsWith("Vocal - ", StringComparison.Ordinal)
                        || named.StartsWith("Instrumental - ", StringComparison.Ordinal)
                    )
                        ? named
                        : null
                )
            : StemFileName.PresetOf(path);

    private static StemTags? ReadFromDisk(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);

            return new StemTags
            {
                Path = path,
                Title = NullIfBlank(file.Tag.Title),
                Artist = NullIfBlank(file.Tag.FirstPerformer),
                Duration =
                    file.Properties.Duration > TimeSpan.Zero ? file.Properties.Duration : null,
                Provenance = StemProvenance.Parse(CommentOf(file)),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Early stemforge runs wrote the provenance to DESCRIPTION, which TagLib does not map to Comment.
    private static string? CommentOf(TagLib.File file) =>
        file.Tag.Comment is { Length: > 0 } comment ? comment
        : file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph
            ? xiph.GetFirstField("DESCRIPTION")
        : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record Entry((long Length, DateTime Written) Stamp, StemTags Tags);
}

/// <summary>What crateside reads off a single stem file.</summary>
public sealed record StemTags
{
    public required string Path { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public TimeSpan? Duration { get; init; }

    /// <summary>Null when the stem carries no StemForge provenance comment.</summary>
    public StemProvenance? Provenance { get; init; }

    /// <summary>The ledger join key, or null when this stem cannot be joined.</summary>
    public string? SourceVideoId => Provenance?.SourceVideoId;
}
