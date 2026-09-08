namespace Crateside.Core.Orchestration;

/// <summary>
/// Everything crateside is holding about one track while that track is in flight: the source
/// audio it acquired and the evidence it produced from it.
/// </summary>
public sealed class WorkingSet(string? root = null)
{
    /// <summary>The evidence from the whole-file detection — the one a verdict run starts from.</summary>
    public const string EvidenceFileName = "evidence.json";

    /// <summary>The largest evidence file worth carrying rather than pointing at.</summary>
    private const long MaxInlineBytes = 32 * 1024;

    private readonly string _root =
        root
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "crateside",
            "working"
        );

    /// <summary>Every video ID currently holding a working set.</summary>
    public IReadOnlyList<string> VideoIds =>
        Directory.Exists(_root)
            ? [.. Directory.EnumerateDirectories(_root).Select(Path.GetFileName).OfType<string>()]
            : [];

    /// <summary>What all working sets together are costing on disk.</summary>
    public long TotalBytes =>
        Directory.Exists(_root)
            ? Directory
                .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Sum(f => TryLength(f))
            : 0;

    /// <summary>The folder for one track, created if it is not there yet.</summary>
    public string Prepare(string videoId)
    {
        var path = DirectoryFor(videoId);

        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>Where the whole-file evidence for this track belongs.</summary>
    public string EvidencePathFor(string videoId) =>
        Path.Combine(DirectoryFor(videoId), EvidenceFileName);

    /// <summary>Whether evidence has been produced for this track — the evidence stage's "done".</summary>
    public bool HasEvidence(string videoId) => File.Exists(EvidencePathFor(videoId));

    /// <summary>
    /// The whole-file evidence itself, when it is small enough to hand to a reader directly.
    /// </summary>
    /// <remarks>
    /// Over the bound this returns false and the caller falls back to naming the path instead.
    /// </remarks>
    public bool TryReadEvidence(string videoId, out string evidence)
    {
        evidence = null!;

        try
        {
            if (
                new FileInfo(EvidencePathFor(videoId)) is
                { Exists: true, Length: <= MaxInlineBytes }
            )
                evidence = File.ReadAllText(EvidencePathFor(videoId));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return evidence is not null;
    }

    /// <summary>
    /// The retained source audio, if it is there.
    /// </summary>
    /// <remarks>
    /// Two audio files in one folder is treated as unknown rather than resolved — this reports
    /// nothing and the caller re-acquires.
    /// </remarks>
    public bool TryGetSource(string videoId, out string path)
    {
        var directory = DirectoryFor(videoId);

        path = (
            Directory.Exists(directory)
                ? Directory
                    .EnumerateFiles(directory)
                    .Where(f =>
                        !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList()
                : []
        )
            is [var only]
            ? only
            : null!;

        return path is not null;
    }

    /// <summary>Throws the whole working set away, once the track is imported.</summary>
    public void Discard(string videoId)
    {
        try
        {
            if (Directory.Exists(DirectoryFor(videoId)))
                Directory.Delete(DirectoryFor(videoId), recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file still open somewhere; harmless, and retried next pass.
        }
    }

    private string DirectoryFor(string videoId) => Path.Combine(_root, videoId);

    private static long TryLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
