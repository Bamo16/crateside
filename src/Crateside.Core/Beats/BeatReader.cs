using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Samples;

namespace Crateside.Core.Beats;

/// <summary>
/// Runs <c>beatdetect</c> with the pinned build on an upload's reference file, its source on disk
/// else an instrumental, and keeps each file's reading until that file changes (#76, #102, ADR 0065).
/// A reading outlives a change of pin, marked as from an older build. Kept across restarts when given a path.
/// </summary>
public sealed partial class BeatReader
{
    // Bump when MeasureAsync changes what it runs, or a kept reading outlives the method that made it.
    private const int Version = 2;

    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private readonly IProcessRunner _processes;
    private readonly string? _path;
    private readonly string _fileName;
    private readonly string _filter;
    private readonly KnownBuilds? _builds;
    private readonly ISourceLocator? _sources;
    private readonly ConcurrentDictionary<string, BeatEntry> _read;
    private readonly Lock _saving = new();
    private volatile bool _changed;
    private volatile FfmpegBuild? _build;
    private int _toRead;
    private int _readSoFar;

    [GeneratedRegex(@"lavfi\.beatdetect\.(?<Key>[a-z_]+?)(?<Index>\d*)=(?<Value>\S+)")]
    private static partial Regex MetadataLine { get; }

    // hpss matched an AI drum stem on the instrumental, measured 2026-09-30 (#83).
    public BeatReader(
        IProcessRunner processes,
        CratesidePaths paths,
        KnownBuilds builds,
        SourceIndex sources
    )
        : this(
            processes,
            paths.BeatCache,
            paths.BeatdetectFfmpeg,
            "beatdetect=hpss=1",
            builds,
            sources
        ) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal BeatReader(
        IProcessRunner processes,
        string? path = null,
        string fileName = "ffmpeg",
        string filter = "beatdetect",
        KnownBuilds? builds = null,
        ISourceLocator? sources = null
    )
    {
        _processes = processes;
        _path = path;
        _fileName = fileName;
        _filter = filter;
        _builds = builds;
        _sources = sources;
        _read = new(Load(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The pinned build new readings are made with, or null until <see cref="RefreshBuildAsync"/> finds one.</summary>
    public FfmpegBuild? Build => _build;

    /// <summary>How far the current pass has got, as files read of those it set out to read.</summary>
    public (int Read, int Of) Progress => (_readSoFar, _toRead);

    /// <summary>Starts counting a pass over this many files.</summary>
    public void Begin(int toRead)
    {
        _toRead = toRead;
        _readSoFar = 0;
    }

    /// <summary>The files with no reading, then those whose reading is from an older build than the pin, each in the order given.</summary>
    public IEnumerable<string> ToRead(IReadOnlyList<string> files) =>
        files.Where(file => Read(file) is null).Concat(files.Where(IsFromOlderBuild));

    /// <summary>The file an upload's beats are read from: its source on disk, else <paramref name="stemPath"/>.</summary>
    public string ReferenceFor(string? videoId, string stemPath) =>
        videoId is not null && _sources?.PathOf(videoId) is { } source && File.Exists(source)
            ? source
            : stemPath;

    /// <summary>The upload's reference file for each stem, once each, in the order given.</summary>
    public IEnumerable<string> ReferencesOf(IEnumerable<string> stemPaths) =>
        stemPaths
            .Select(stem => ReferenceFor(StemTagReader.Read(stem)?.Provenance?.SourceVideoId, stem))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The reference file's reading; until that is read, the stem's own, which belongs to the stem.
    /// </summary>
    public BeatReading? ReadFor(string? videoId, string stemPath) =>
        Read(ReferenceFor(videoId, stemPath)) ?? Read(stemPath);

    /// <summary>The kept reading, whichever build made it, or null when the file changed since.</summary>
    public BeatReading? Read(string audioPath) => Kept(audioPath)?.Reading;

    /// <summary>The build that made the kept reading; null when there is none or its build was never identified.</summary>
    public FfmpegBuild? BuildOf(string audioPath) => Kept(audioPath)?.Build;

    /// <summary>True when the kept reading was made by an older build than the pinned one, or one never identified.</summary>
    public bool IsFromOlderBuild(string audioPath) =>
        Kept(audioPath) is { } entry
        && _build is { } pinned
        && (entry.Build is null || entry.Build.IsOlderThan(pinned));

    /// <summary>Carries a reading to where its file was moved, since the audio itself is unchanged.</summary>
    public void Moved(string from, string to)
    {
        if (_read.TryRemove(Path.GetFullPath(from), out var entry))
        {
            _read[Path.GetFullPath(to)] = entry;
            _changed = true;
        }
    }

    /// <summary>Asks the pinned ffmpeg which build it is; null when it cannot be run.</summary>
    public async Task<FfmpegBuild?> RefreshBuildAsync(CancellationToken ct)
    {
        if (_builds is not null)
            await _builds.SeedAsync(ct);

        return _build = await FfmpegBuild.IdentifyAsync(_processes, PinnedFileName, ct);
    }

    /// <summary>Reads the whole file, then each full 30 s of it; a file ffmpeg cannot read keeps an empty reading.</summary>
    public async Task<BeatReading> MeasureAsync(string audioPath, CancellationToken ct)
    {
        var file = new FileInfo(audioPath);
        var stamp = BeatStamp.Of(file);
        var whole = await DetectAsync(file.FullName, null, ct);
        var windows = new List<double>();

        foreach (var start in WindowStarts(SecondsOf(file.FullName)))
        {
            if (
                await DetectAsync(file.FullName, start, ct) is
                { BeatIntervalMs: > 0 and var interval }
            )
                windows.Add(60_000 / interval);
        }

        var reading = new BeatReading
        {
            Bpm = whole.Bpm,
            Confidence = whole.Confidence,
            TempoSpread = whole.TempoSpread,
            BeatOffsetMs = whole.BeatOffsetMs,
            DownbeatOffsetMs = whole.DownbeatOffsetMs,
            DownbeatConfidence = whole.DownbeatConfidence,
            BeatsPerBar = whole.BeatsPerBar,
            TempoWander = whole.TempoWander,
            Candidates = whole.Candidates,
            Segments = whole.Segments,
            Windows = windows,
        };

        _read[file.FullName] = new BeatEntry(stamp, _build, reading);
        _changed = true;
        Interlocked.Increment(ref _readSoFar);

        return reading;
    }

    /// <summary>Writes what was read since the last save, leaving out files that are gone.</summary>
    public void Save()
    {
        if (_path is null || !_changed)
            return;

        lock (_saving)
        {
            _changed = false;

            var kept = new BeatFile(
                Version,
                _read
                    .Where(pair => File.Exists(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            );

            try
            {
                BeatJsonContext.Default.BeatFile.WriteFile(_path, kept);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _changed = true;
            }
        }
    }

    /// <summary>The instrumentals to read, staging's before the library's.</summary>
    public static IEnumerable<string> Instrumentals(
        CratesidePaths paths,
        FolderInfoReader folderInfo
    ) =>
        Instrumentals(paths.StagingFolder, SearchOption.TopDirectoryOnly, folderInfo)
            .Concat(Instrumentals(paths.LibraryFolder, SearchOption.AllDirectories, folderInfo));

    /// <summary>
    /// Ableton's Type says so, which a kept source used as the instrumental carries too; an untagged
    /// file, fresh in staging, by its name.
    /// </summary>
    public static bool IsInstrumental(string file, FileTags? tags) =>
        tags?.Type is { } type
            ? type.Equals("Instrumental", StringComparison.OrdinalIgnoreCase)
            : Path.GetFileName(file).Contains("(Instrumental", StringComparison.OrdinalIgnoreCase);

    /// <summary>The last value the filter reported for each figure, from its printed metadata.</summary>
    internal static Detected Parse(string printed)
    {
        var last = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (Match line in MetadataLine.Matches(printed))
        {
            if (
                line.Groups["Index"] is { Length: 0 }
                && double.TryParse(
                    line.Groups["Value"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
                last[line.Groups["Key"].Value] = value;
        }

        var final = MetadataLine.Matches(LastFrame(printed));

        return new Detected(
            last.TryGetValue("bpm", out var bpm) && bpm > 0 ? bpm : null,
            last.TryGetValue("confidence", out var confidence) ? confidence : null,
            last.TryGetValue("beat_interval_ms", out var interval) ? interval : null,
            last.TryGetValue("tempo_spread", out var spread) ? spread : null
        )
        {
            BeatOffsetMs = last.TryGetValue("beat_offset_ms", out var offset) ? offset : null,
            DownbeatOffsetMs = last.TryGetValue("downbeat_offset_ms", out var downbeat)
                ? downbeat
                : null,
            DownbeatConfidence = last.TryGetValue("downbeat_confidence", out var sure)
                ? sure
                : null,
            BeatsPerBar = last.TryGetValue("beats_per_bar", out var perBar) ? (int)perBar : null,
            TempoWander = last.TryGetValue("tempo_wander", out var wander) ? wander : null,
            Candidates =
            [
                .. Indexed(final, "candidate")
                    .Select(value => BeatCandidate.TryParse(value))
                    .OfType<BeatCandidate>(),
            ],
            Segments =
            [
                .. Indexed(final, "tempo_segment")
                    .Select(value => TempoSegment.TryParse(value))
                    .OfType<TempoSegment>(),
            ],
        };
    }

    /// <summary>Every full window's start; a file shorter than one window is read once from the top.</summary>
    internal static IEnumerable<int> WindowStarts(double seconds)
    {
        var max = Math.Max((int)seconds - BeatReading.WindowSeconds + 1, 1);

        for (var start = 0; start < max; start += BeatReading.WindowSeconds)
            yield return start;
    }

    private static string LastFrame(string printed) =>
        printed.LastIndexOf("frame:", StringComparison.Ordinal) is var at and >= 0
            ? printed[at..]
            : printed;

    private static IEnumerable<string> Indexed(MatchCollection lines, string key) =>
        lines
            .Where(line => line.Groups["Key"].Value == key && line.Groups["Index"].Length > 0)
            .OrderBy(line => int.Parse(line.Groups["Index"].Value, CultureInfo.InvariantCulture))
            .Select(line => line.Groups["Value"].Value);

    private string PinnedFileName => _builds?.Pinned?.Path ?? _fileName;

    private BeatEntry? Kept(string audioPath) =>
        new FileInfo(audioPath) is { Exists: true } file
        && _read.TryGetValue(file.FullName, out var entry)
        && entry.Stamp == BeatStamp.Of(file)
            ? entry
            : null;

    private async Task<Detected> DetectAsync(string audioPath, int? start, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(RunLimit);

        string[] window = start is { } seconds
            ?
            [
                "-ss",
                seconds.ToString(CultureInfo.InvariantCulture),
                "-t",
                $"{BeatReading.WindowSeconds}",
            ]
            : [];

        try
        {
            var outcome = await _processes.RunAsync(
                new ProcessRequest(
                    PinnedFileName,
                    [
                        "-hide_banner",
                        "-nostats",
                        .. window,
                        "-i",
                        audioPath,
                        "-vn",
                        "-af",
                        $"{_filter},ametadata=mode=print:file=-",
                        "-f",
                        "null",
                        "-",
                    ]
                ),
                limit.Token
            );

            return Parse(outcome.StandardOutput);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Detected(null, null, null, null);
        }
    }

    private static IEnumerable<string> Instrumentals(
        string root,
        SearchOption depth,
        FolderInfoReader folderInfo
    )
    {
        if (!Directory.Exists(root))
            return [];

        var tags = AbletonReads.TagsOrEmpty(folderInfo.Read, root);

        return Directory
            .EnumerateFiles(root, "*", depth)
            .Where(file =>
                SampleFiles.IsAudio(file)
                && IsInstrumental(file, tags.TryGet(file, out var found) ? found : null)
            );
    }

    private static double SecondsOf(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);

            return file.Properties.Duration.TotalSeconds;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static Dictionary<string, BeatEntry> Load(string? path) =>
        path is not null
        && BeatJsonContext.Default.BeatFile.ReadFileOrDefault(path)
            is { Version: Version, Entries: { } entries }
            ? entries
            : [];
}

internal readonly record struct Detected(
    double? Bpm,
    double? Confidence,
    double? BeatIntervalMs,
    double? TempoSpread
)
{
    public double? BeatOffsetMs { get; init; }
    public double? DownbeatOffsetMs { get; init; }
    public double? DownbeatConfidence { get; init; }
    public int? BeatsPerBar { get; init; }
    public double? TempoWander { get; init; }
    public IReadOnlyList<BeatCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<TempoSegment> Segments { get; init; } = [];
}

internal sealed record BeatStamp(long Length, DateTime Written)
{
    public static BeatStamp Of(FileInfo file) => new(file.Length, file.LastWriteTimeUtc);
}

internal sealed record BeatEntry(BeatStamp Stamp, FfmpegBuild? Build, BeatReading Reading);

internal sealed record BeatFile(int Version, Dictionary<string, BeatEntry> Entries);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(BeatFile))]
[JsonSerializable(typeof(BuildInfo))]
[JsonSerializable(typeof(KnownBuildsFile))]
internal sealed partial class BeatJsonContext : JsonSerializerContext { }
