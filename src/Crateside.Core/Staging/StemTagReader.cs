namespace Crateside.Core.Staging;

/// <summary>
/// Reads a stem's tags off disk with TagLibSharp — in-process, no external binary.
/// </summary>
/// <remarks>
/// Reads through <c>Tag.Comment</c>, exactly as StemForge writes it, rather than reaching
/// for the Xiph field by name: the round-trip is already proven by StemForge's own
/// local-file path, and it keeps this reader working if a stem is ever not FLAC.
/// </remarks>
public sealed class StemTagReader
{
    /// <summary>
    /// Reads the tags crateside needs from a stem file. Returns null if the file is
    /// unreadable or its format is unsupported — a stem the board cannot read is a stem
    /// with no key and no video ID, which the staging board renders as unknown rather than
    /// as an error.
    /// </summary>
    public StemTags? Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);

            return new StemTags
            {
                Path = path,
                Title = NullIfBlank(file.Tag.Title),
                Artist = NullIfBlank(file.Tag.FirstPerformer),
                Duration = file.Properties.Duration > TimeSpan.Zero
                    ? file.Properties.Duration
                    : null,
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
