using System.Collections.Concurrent;
using Crateside.Core.Imports;
using Crateside.Core.Orchestration.Download;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Everything crateside is holding about one track while that track is in flight: the source
/// audio it acquired and the evidence it produced from it.
/// </summary>
public sealed class WorkingSet(CratesidePaths paths, TimeProvider? time = null)
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

    /// <summary>A folder modified this recently is listed again whatever it was last read at, since a second change can land in the same timestamp tick.</summary>
    private static readonly TimeSpan Racy = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, (DateTime Written, string? Source)> _listed = new(
        StringComparer.Ordinal
    );

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The largest evidence file worth carrying rather than pointing at.</summary>
    private const long MaxInlineBytes = 32 * 1024;

    private readonly string _root = paths.WorkingFolder;
    private readonly string _imports = paths.ImportsFolder;
    private readonly string _judging = paths.JudgingFolder;

    internal WorkingSet(string root, TimeProvider? time = null)
        : this(
            new CratesidePaths
            {
                WorkingFolder = root,
                ImportsFolder = Path.Combine(Path.GetDirectoryName(root) ?? root, "imports"),
                JudgingFolder = Path.Combine(Path.GetDirectoryName(root) ?? root, "judging"),
            },
            time
        ) { }

    /// <summary>Every video ID currently holding a working set.</summary>
    public IReadOnlyList<string> TrackKeys =>
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
    public string Prepare(string trackKey)
    {
        var path = DirectoryFor(trackKey);

        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>
    /// The folder a judge session runs in and writes its verdict to, created if it is not there yet.
    /// Its name has no <c>~</c>: Claude Code refuses to write a path with a tilde before a digit
    /// (it reads as a Windows 8.3 short name), and an imported track's key is <c>file~</c> and hex (ADR 0084).
    /// </summary>
    public string JudgeFolderFor(string trackKey) =>
        Directory.CreateDirectory(JudgingPath(trackKey)).FullName;

    /// <summary>Where the whole-file evidence for this track belongs.</summary>
    public string EvidencePathFor(string trackKey) =>
        Path.Combine(DirectoryFor(trackKey), EvidenceFileName);

    /// <summary>Whether evidence has been produced for this track — the evidence stage's "done".</summary>
    public bool HasEvidence(string trackKey) => File.Exists(EvidencePathFor(trackKey));

    /// <summary>
    /// The whole-file evidence itself, when it is small enough to hand to a reader directly.
    /// </summary>
    public bool TryReadEvidence(string trackKey, out string evidence)
    {
        evidence = null!;

        try
        {
            if (
                new FileInfo(EvidencePathFor(trackKey)) is
                { Exists: true, Length: <= MaxInlineBytes }
            )
                evidence = File.ReadAllText(EvidencePathFor(trackKey));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return evidence is not null;
    }

    /// <summary>Where the download's own account of itself belongs.</summary>
    public string AcquisitionPathFor(string trackKey) =>
        Path.Combine(DirectoryFor(trackKey), AcquisitionFileName);

    /// <summary>
    /// How this track's retained download went, or null — no finding — when nothing recorded it.
    /// </summary>
    public Acquisition? AcquisitionFor(string trackKey) =>
        WorkingSetJsonContext.Default.Acquisition.ReadFileOrDefault(AcquisitionPathFor(trackKey));

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
            var trackKey = directory.Name;
            var written = directory.LastWriteTimeUtc;

            if (_acquisitions.TryGetValue(trackKey, out var kept) && kept.Written == written)
            {
                if (kept.Acquisition is { } acquisition)
                    found[trackKey] = acquisition;

                continue;
            }

            var read = AcquisitionFor(trackKey);

            if (read is not null || !File.Exists(AcquisitionPathFor(trackKey)))
                _acquisitions[trackKey] = (written, read);

            if (read is not null)
                found[trackKey] = read;
        }

        return found;
    }

    /// <summary>Writes down how the download went, replacing whatever was there.</summary>
    public void SaveAcquisition(string trackKey, Acquisition acquisition)
    {
        _acquisitions.TryRemove(trackKey, out _);

        try
        {
            WorkingSetJsonContext.Default.Acquisition.WriteFile(
                AcquisitionPathFor(trackKey),
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
    public bool AcceptAcquisition(string trackKey)
    {
        if (AcquisitionFor(trackKey) is not { } acquisition)
            return false;

        SaveAcquisition(trackKey, acquisition with { IsAccepted = true });

        return true;
    }

    /// <summary>
    /// The retained source audio, if exactly one file of it is there, else the file imported for
    /// the track (ADR 0077).
    /// </summary>
    public bool TryGetSource(string trackKey, out string path)
    {
        var directory = DirectoryFor(trackKey);

        path = (Directory.Exists(directory) ? OnlySourceIn(directory) : null)!;

        if (path is null && ImportStore.TryFind(_imports, trackKey, out var imported))
            path = imported;

        return path is not null;
    }

    /// <summary>
    /// <see cref="TryGetSource"/> for every track at once, in one listing of each root. A folder's
    /// files are listed again only when its modified time moves, because opening a folder costs
    /// about a third of a millisecond on this machine and there is one per track.
    /// </summary>
    public IReadOnlyDictionary<string, string> Sources()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var directory in FoldersIn(_root))
            if (Listed("working", directory, OnlySourceIn) is { } source)
                found[directory.Name] = source;

        foreach (var directory in FoldersIn(_imports))
            if (
                !found.ContainsKey(directory.Name)
                && Listed("imports", directory, FirstFileIn) is { } imported
            )
                found[directory.Name] = imported;

        return found;
    }

    private string? Listed(string kind, DirectoryInfo directory, Func<string, string?> read)
    {
        var key = $"{kind}/{directory.FullName}";
        var written = directory.LastWriteTimeUtc;

        if (_listed.TryGetValue(key, out var kept) && kept.Written == written)
            return kept.Source;

        var source = read(directory.FullName);

        if (_time.GetUtcNow().UtcDateTime - written > Racy)
            _listed[key] = (written, source);

        return source;
    }

    private static IEnumerable<DirectoryInfo> FoldersIn(string root) =>
        Directory.Exists(root) ? new DirectoryInfo(root).EnumerateDirectories() : [];

    private static string? OnlySourceIn(string directory) =>
        Directory
            .EnumerateFiles(directory)
            .Where(f =>
                !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            )
            .ToList()
            is [var only]
            ? only
            : null;

    private static string? FirstFileIn(string directory) =>
        Directory.EnumerateFiles(directory).FirstOrDefault();

    /// <summary>When the track left staging, or null while no clock is running for it.</summary>
    public DateTimeOffset? LeftStagingAt(string trackKey) =>
        WorkingSetJsonContext
            .Default.LeftStaging.ReadFileOrDefault(
                Path.Combine(DirectoryFor(trackKey), LeftStagingFileName)
            )
            ?.At;

    /// <summary>How long retention keeps this track's source at least: a day for a fetch the person asked for, else nothing (ADR 0094).</summary>
    public TimeSpan MinimumRetention(string trackKey) =>
        WorkingSetJsonContext.Default.LeftStaging.ReadFileOrDefault(
            Path.Combine(DirectoryFor(trackKey), LeftStagingFileName)
        )
            is { AdHoc: true }
            ? TimeSpan.FromDays(1)
            : TimeSpan.Zero;

    /// <param name="adHoc">A fetch the person asked for, which retention never keeps for less than a day (ADR 0094).</param>
    public void RecordLeftStaging(string trackKey, DateTimeOffset at, bool adHoc = false)
    {
        try
        {
            WorkingSetJsonContext.Default.LeftStaging.WriteFile(
                Path.Combine(DirectoryFor(trackKey), LeftStagingFileName),
                new LeftStaging(at, adHoc)
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The clock starts at the next sweep instead.
        }
    }

    public void ClearLeftStaging(string trackKey)
    {
        try
        {
            File.Delete(Path.Combine(DirectoryFor(trackKey), LeftStagingFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Throws the whole working set away, once the track is imported.</summary>
    public void Discard(string trackKey)
    {
        try
        {
            if (Directory.Exists(DirectoryFor(trackKey)))
                Directory.Delete(DirectoryFor(trackKey), recursive: true);

            if (Directory.Exists(JudgingPath(trackKey)))
                Directory.Delete(JudgingPath(trackKey), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still open somewhere; harmless, and retried next pass.
        }
    }

    private string DirectoryFor(string trackKey) => Path.Combine(_root, trackKey);

    private string JudgingPath(string trackKey) =>
        Path.Combine(_judging, trackKey.Replace('~', '-'));

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
public sealed record LeftStaging(DateTimeOffset At, bool AdHoc = false);
