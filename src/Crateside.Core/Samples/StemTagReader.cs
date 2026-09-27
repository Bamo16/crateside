namespace Crateside.Core.Samples;

/// <summary>
/// Reads a stem's tags off disk with TagLibSharp — in-process, no external binary.
/// </summary>
public static class StemTagReader
{
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

    /// <summary>
    /// Reads the tags crateside needs from a stem file. Returns null if the file is
    /// unreadable or its format is unsupported.
    /// </summary>
    public static StemTags? Read(string path)
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
