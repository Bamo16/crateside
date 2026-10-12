using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Samples;

namespace Crateside.Core.Beats;

/// <summary>
/// Runs <c>beatdetect</c> on an instrumental and keeps the reading until the file or the ffmpeg
/// build changes (#76). Kept across restarts when given a path.
/// </summary>
public sealed partial class BeatReader(
    string? path = null,
    string fileName = "ffmpeg",
    string filter = "beatdetect"
)
{
    // Bump when MeasureAsync changes what it runs, or a kept reading outlives the method that made it.
    private const int Version = 2;

    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private readonly string? _path = path;
    private readonly ConcurrentDictionary<string, BeatEntry> _read = new(
        Load(path),
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Lock _saving = new();
    private volatile bool _changed;
    private volatile FfmpegBuild? _build;
    private int _toRead;
    private int _readSoFar;

    [GeneratedRegex(@"lavfi\.beatdetect\.(?<Key>\w+)=(?<Value>[0-9.]+)")]
    private static partial Regex MetadataLine { get; }

    [GeneratedRegex(@"^ffmpeg version (?<Version>\S+)")]
    private static partial Regex VersionLine { get; }

    /// <summary>The build readings are current for, or null until <see cref="RefreshBuildAsync"/> finds one.</summary>
    public FfmpegBuild? Build => _build;

    /// <summary>How far the current pass has got, as instrumentals read of those that had no reading.</summary>
    public (int Read, int Of) Progress => (_readSoFar, _toRead);

    /// <summary>Starts counting a pass over this many instrumentals.</summary>
    public void Begin(int unread)
    {
        _toRead = unread;
        _readSoFar = 0;
    }

    /// <summary>The kept reading, or null when the file changed since or was read by another build.</summary>
    public BeatReading? Read(string audioPath) =>
        new FileInfo(audioPath) is { Exists: true } file
        && _read.TryGetValue(file.FullName, out var entry)
        && entry.Stamp == BeatStamp.Of(file)
        && (_build is null || entry.Build == _build)
            ? entry.Reading
            : null;

    /// <summary>Carries a reading to where its file was moved, since the audio itself is unchanged.</summary>
    public void Moved(string from, string to)
    {
        if (_read.TryRemove(Path.GetFullPath(from), out var entry))
        {
            _read[Path.GetFullPath(to)] = entry;
            _changed = true;
        }
    }

    /// <summary>Asks ffmpeg which build it is; null when it cannot be run.</summary>
    public async Task<FfmpegBuild?> RefreshBuildAsync(CancellationToken ct)
    {
        var outcome = await ExternalProcess.RunAsync(fileName, ["-version"], ct: ct);

        if (
            !outcome.Succeeded
            || VersionLine.Match(outcome.StandardOutput) is not { Success: true } line
            || Located() is not { Exists: true } executable
        )
            return _build = null;

        var sha256 = await HashAsync(executable, ct);
        var commit =
            Path.Combine(executable.DirectoryName ?? string.Empty, "build.json") is var info
            && BeatJsonContext.Default.BuildInfo.ReadFileOrDefault(info)
                is { Sha256: { } recorded, Commit: { } recordedCommit }
            && recorded.Equals(sha256, StringComparison.OrdinalIgnoreCase)
                ? recordedCommit
                : null;

        return _build = new FfmpegBuild(line.Groups["Version"].Value, sha256, commit);
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
        Instrumentals(paths.SampleStaging, SearchOption.TopDirectoryOnly, folderInfo)
            .Concat(Instrumentals(paths.MashupSamples, SearchOption.AllDirectories, folderInfo));

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
                double.TryParse(
                    line.Groups["Value"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
                last[line.Groups["Key"].Value] = value;
        }

        return new Detected(
            last.TryGetValue("bpm", out var bpm) && bpm > 0 ? bpm : null,
            last.TryGetValue("confidence", out var confidence) ? confidence : null,
            last.TryGetValue("beat_interval_ms", out var interval) ? interval : null,
            last.TryGetValue("tempo_spread", out var spread) ? spread : null
        );
    }

    /// <summary>Every full window's start; a file shorter than one window is read once from the top.</summary>
    internal static IEnumerable<int> WindowStarts(double seconds)
    {
        var max = Math.Max((int)seconds - BeatReading.WindowSeconds + 1, 1);

        for (var start = 0; start < max; start += BeatReading.WindowSeconds)
            yield return start;
    }

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
            var outcome = await ExternalProcess.RunAsync(
                fileName,
                [
                    "-hide_banner",
                    "-nostats",
                    .. window,
                    "-i",
                    audioPath,
                    "-vn",
                    "-af",
                    $"{filter},ametadata=mode=print:file=-",
                    "-f",
                    "null",
                    "-",
                ],
                ct: limit.Token
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

    private FileInfo? Located() =>
        Path.IsPathRooted(fileName)
            ? new FileInfo(fileName)
            : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => new FileInfo(
                    Path.Combine(
                        folder,
                        OperatingSystem.IsWindows() && !Path.HasExtension(fileName)
                            ? $"{fileName}.exe"
                            : fileName
                    )
                ))
                .FirstOrDefault(candidate => candidate.Exists);

    private static async Task<string> HashAsync(FileInfo executable, CancellationToken ct)
    {
        await using var stream = executable.OpenRead();

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
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

/// <summary>Which ffmpeg read a file: its version and the executable's hash, with the commit when a <c>build.json</c> beside it vouches for that hash.</summary>
public sealed record FfmpegBuild(string Version, string Sha256, string? Commit)
{
    public string Label => Commit is { Length: >= 7 } ? $"{Version}-{Commit[..7]}" : Version;
}

internal sealed record BuildInfo(string? Commit, string? Sha256);

internal readonly record struct Detected(
    double? Bpm,
    double? Confidence,
    double? BeatIntervalMs,
    double? TempoSpread
);

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
internal sealed partial class BeatJsonContext : JsonSerializerContext { }
