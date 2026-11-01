using System.Collections.Concurrent;
using Crateside.Core.Orchestration.Download;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Everything crateside is holding about one track while that track is in flight: the source
/// audio it acquired and the evidence it produced from it.
/// </summary>
public sealed class WorkingSet(string? root = null)
{
    /// <summary>The evidence from the whole-file detection — the one a verdict run starts from.</summary>
    public const string EvidenceFileName = "evidence.json";

    /// <summary>How the download went, as <c>stemforge-cli</c> reported it.</summary>
    public const string AcquisitionFileName = "acquisition.json";

    /// <summary>When the track's last stem left staging, which starts the source's retention clock (#133).</summary>
    public const string LeftStagingFileName = "left-staging.json";

    private readonly ConcurrentDictionary<
        string,
        (DateTime Written, Acquisition? Acquisition)
    > _acquisitions = new(StringComparer.Ordinal);

    /// <summary>The largest evidence file worth carrying rather than pointing at.</summary>
    private const long MaxInlineBytes = 32 * 1024;

    private readonly string _root =
        root
        ?? Environment.SpecialFolder.LocalApplicationData.GetFolderPath("crateside", "working");

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return evidence is not null;
    }

    /// <summary>Where the download's own account of itself belongs.</summary>
    public string AcquisitionPathFor(string videoId) =>
        Path.Combine(DirectoryFor(videoId), AcquisitionFileName);

    /// <summary>
    /// How this track's retained download went, or null — no finding — when nothing recorded it.
    /// </summary>
    public Acquisition? AcquisitionFor(string videoId) =>
        WorkingSetJsonContext.Default.Acquisition.ReadFileOrDefault(AcquisitionPathFor(videoId));

    /// <summary>
    /// Every recorded acquisition, by video ID — what the rows are drawn from. A folder's is read
    /// again only when the folder's modified time moves or this instance saved into it, because a stat per folder costs as much
    /// as the read and every write goes through a temporary file that moves it.
    /// </summary>
    public IReadOnlyDictionary<string, Acquisition> Acquisitions()
    {
        var found = new Dictionary<string, Acquisition>(StringComparer.Ordinal);

        if (!Directory.Exists(_root))
            return found;

        foreach (var directory in new DirectoryInfo(_root).EnumerateDirectories())
        {
            var videoId = directory.Name;
            var written = directory.LastWriteTimeUtc;

            if (_acquisitions.TryGetValue(videoId, out var kept) && kept.Written == written)
            {
                if (kept.Acquisition is { } acquisition)
                    found[videoId] = acquisition;

                continue;
            }

            var read = AcquisitionFor(videoId);

            if (read is not null || !File.Exists(AcquisitionPathFor(videoId)))
                _acquisitions[videoId] = (written, read);

            if (read is not null)
                found[videoId] = read;
        }

        return found;
    }

    /// <summary>Writes down how the download went, replacing whatever was there.</summary>
    public void SaveAcquisition(string videoId, Acquisition acquisition)
    {
        _acquisitions.TryRemove(videoId, out _);

        try
        {
            WorkingSetJsonContext.Default.Acquisition.WriteFile(
                AcquisitionPathFor(videoId),
                acquisition
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing this costs a finding, not the download it describes.
        }
    }

    /// <summary>
    /// Records that the user has accepted this download's audio, and says whether there was one
    /// to accept.
    /// </summary>
    public bool AcceptAcquisition(string videoId)
    {
        if (AcquisitionFor(videoId) is not { } acquisition)
            return false;

        SaveAcquisition(videoId, acquisition with { IsAccepted = true });

        return true;
    }

    /// <summary>
    /// The retained source audio, if exactly one file of it is there.
    /// </summary>
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

    /// <summary>When the track left staging, or null while no clock is running for it.</summary>
    public DateTimeOffset? LeftStagingAt(string videoId) =>
        WorkingSetJsonContext
            .Default.LeftStaging.ReadFileOrDefault(
                Path.Combine(DirectoryFor(videoId), LeftStagingFileName)
            )
            ?.At;

    public void RecordLeftStaging(string videoId, DateTimeOffset at)
    {
        try
        {
            WorkingSetJsonContext.Default.LeftStaging.WriteFile(
                Path.Combine(DirectoryFor(videoId), LeftStagingFileName),
                new LeftStaging(at)
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The clock starts at the next sweep instead.
        }
    }

    public void ClearLeftStaging(string videoId)
    {
        try
        {
            File.Delete(Path.Combine(DirectoryFor(videoId), LeftStagingFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Throws the whole working set away, once the track is imported.</summary>
    public void Discard(string videoId)
    {
        try
        {
            if (Directory.Exists(DirectoryFor(videoId)))
                Directory.Delete(DirectoryFor(videoId), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

/// <summary>The moment a track's last stem left staging.</summary>
public sealed record LeftStaging(DateTimeOffset At);
