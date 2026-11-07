using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Loudness;

/// <summary>
/// Reads a file's integrated loudness (EBU R128, whole file) with ffmpeg's <c>ebur128</c>, and keeps
/// it until the file changes (ADR 0059). Kept across restarts when given a path.
/// </summary>
public sealed partial class LoudnessReader
{
    // Bump when MeasureAsync changes what it runs, or a kept reading outlives the method that made it.
    private const int Version = 1;

    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(5);

    private readonly IProcessRunner _runner;
    private readonly string? _path;
    private readonly string _fileName;
    private readonly ConcurrentDictionary<string, LoudnessEntry> _read;
    private readonly Lock _saving = new();
    private volatile bool _changed;

    [GeneratedRegex(
        @"Integrated\ loudness:\s+I:\s+(?<Lufs>-?\d+(\.\d+)?)\ LUFS",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex Integrated { get; }

    public LoudnessReader(IProcessRunner runner, CratesidePaths paths)
        : this(runner, paths.LoudnessCache) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal LoudnessReader(IProcessRunner runner, string? path = null, string fileName = "ffmpeg")
    {
        _runner = runner;
        _path = path;
        _fileName = fileName;
        _read = new(Load(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The kept reading, or null when there is none or the file changed since.</summary>
    public LoudnessReading? Read(string audioPath) =>
        new FileInfo(audioPath) is { Exists: true } file
        && _read.TryGetValue(file.FullName, out var entry)
        && entry.Stamp == LoudnessStamp.Of(file)
            ? entry.Reading
            : null;

    /// <summary>
    /// Reads the whole file and keeps the reading, an empty one when ffmpeg finds no loudness in it;
    /// null, and nothing kept, when the file is gone or ffmpeg cannot be run.
    /// </summary>
    public async Task<LoudnessReading?> MeasureAsync(string audioPath, CancellationToken ct)
    {
        if (new FileInfo(audioPath) is not { Exists: true } file)
            return null;

        var stamp = LoudnessStamp.Of(file);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(RunLimit);
        ProcessOutcome outcome;

        try
        {
            outcome = await _runner.RunAsync(
                new ProcessRequest(
                    _fileName,
                    [
                        "-hide_banner",
                        "-nostats",
                        "-i",
                        file.FullName,
                        "-vn",
                        "-af",
                        "ebur128=framelog=quiet",
                        "-f",
                        "null",
                        "-",
                    ]
                ),
                limit.Token
            );
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            outcome = new ProcessOutcome
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
            };
        }

        var reading = new LoudnessReading(outcome.Succeeded ? Parse(outcome.StandardError) : null);

        _read[file.FullName] = new LoudnessEntry(stamp, reading);
        _changed = true;

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

            var kept = new LoudnessFile(
                Version,
                _read
                    .Where(pair => File.Exists(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            );

            try
            {
                LoudnessJsonContext.Default.LoudnessFile.WriteFile(_path, kept);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _changed = true;
            }
        }
    }

    /// <summary>The integrated loudness in the summary <c>ebur128</c> prints when it ends.</summary>
    internal static double? Parse(string printed) =>
        Integrated.Match(printed) is { Success: true } match
        && double.TryParse(
            match.Groups["Lufs"].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var lufs
        )
            ? lufs
            : null;

    private static Dictionary<string, LoudnessEntry> Load(string? path) =>
        path is not null
        && LoudnessJsonContext.Default.LoudnessFile.ReadFileOrDefault(path)
            is { Version: Version, Entries: { } entries }
            ? entries
            : [];
}

/// <param name="IntegratedLufs">Null when ffmpeg could not read the file.</param>
public sealed record LoudnessReading(double? IntegratedLufs);

internal sealed record LoudnessStamp(long Length, DateTime Written)
{
    public static LoudnessStamp Of(FileInfo file) => new(file.Length, file.LastWriteTimeUtc);
}

internal sealed record LoudnessEntry(LoudnessStamp Stamp, LoudnessReading Reading);

internal sealed record LoudnessFile(int Version, Dictionary<string, LoudnessEntry> Entries);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(LoudnessFile))]
internal sealed partial class LoudnessJsonContext : JsonSerializerContext { }
