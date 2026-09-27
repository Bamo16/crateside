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
                Provenance = StemProvenance.Parse(file.Tag.Comment),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

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
