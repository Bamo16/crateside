namespace Crateside.Core.Orchestration;

/// <summary>
/// Everything crateside is holding about one track while that track is in flight: the source
/// audio it acquired and the evidence it produced from it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scratch, never a record.</strong> Losing a working set costs a re-run and never a
/// fact — the verdict lives in keytag's ledger and the job history lives in
/// <see cref="JobStore"/>. It is safe to delete by hand at any time, which is why it sits under
/// <c>%LOCALAPPDATA%</c> rather than beside the roaming settings: this is cache.
/// </para>
/// <para>
/// It exists because splitting evidence from judgment <em>in time</em> leaves the machine layer
/// with nowhere to live. keytag persists one only inside a finished run record, since the agent
/// used to produce both halves in one session. An evidence store in keytag would have exactly
/// one writer — crateside — which is the dependency reversal the boundary exists to prevent, so
/// crateside keeps it instead (ADR 0005).
/// </para>
/// <para>
/// One folder per video ID, holding one audio file and one or more evidence JSONs. The audio is
/// not named by us: <c>stemforge-cli download</c> names it from the release metadata, and that
/// name is worth keeping because the separator reads the same tags to name the stems.
/// </para>
/// </remarks>
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

    /// <summary>
    /// What the working sets are costing on disk. Surfaced because the retention window runs
    /// until a track is imported, which can be weeks — this should be visible rather than
    /// quietly accumulating.
    /// </summary>
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
    /// <para>
    /// For inlining into the verdict prompt. A session reading this file spends a whole round
    /// trip on it — measured at ~25k of context re-read — where the file is under a thousand
    /// tokens sitting in a prefix that is cached anyway. So the cheap thing is to carry it.
    /// </para>
    /// <para>
    /// <strong>The bound is not about any file seen so far.</strong> Real evidence is 2–4 KB of
    /// fixed shape from <c>keytag-detect</c>; the cap is there because <c>key_events</c> is a
    /// flicker list with no length in its definition, and one pathological track must not
    /// quietly turn the saving back into a cost. Over the bound this reports nothing and the
    /// prompt falls back to naming the path, which is what it did before and still works.
    /// </para>
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable evidence is not worth failing over here: the caller names the path
            // instead, and the session reads it the old way.
        }

        return evidence is not null;
    }

    /// <summary>
    /// The retained source audio, if it is there.
    /// </summary>
    /// <remarks>
    /// Found by elimination rather than by name, because the downloader chooses the name. Two
    /// audio files in one folder is not a case to reconcile — it means the folder is not what we
    /// think it is, so this reports nothing and the caller re-acquires, which is cheap and
    /// always correct.
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

    /// <summary>
    /// Throws the whole working set away. Called when the track is <em>imported</em> — the point
    /// at which there is nothing further to do with the track, and so nothing further to do with
    /// what was gathered about it.
    /// </summary>
    public void Discard(string videoId)
    {
        try
        {
            if (Directory.Exists(DirectoryFor(videoId)))
                Directory.Delete(DirectoryFor(videoId), recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file still open somewhere. Scratch that outlives its welcome costs disk and
            // nothing else, and the next pass will try again.
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
