using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// Every sample crateside sent to the recycle bin on its own initiative, in
/// <c>%APPDATA%\crateside\deletions.jsonl</c>.
/// </summary>
public sealed class DeletionLog(CratesidePaths paths, TimeProvider? time = null)
{
    private readonly string _path = paths.DeletionsFile;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    internal DeletionLog(string path, TimeProvider? time = null)
        : this(new CratesidePaths { DeletionsFile = path }, time) { }

    /// <summary>Recycles without recording, for a caller with nowhere to say it.</summary>
    public static DeletionLog None { get; } = new(string.Empty);

    /// <summary>
    /// Sends a sample to the recycle bin and records which upload it was of — read before it goes
    /// — and why.
    /// </summary>
    public void Recycle(string sample, string reason)
    {
        if (!File.Exists(sample))
            return;

        var trackKey = StemTagReader.Read(sample)?.SourceTrackKey;

        RecycleBin.Remove(sample);

        Append(
            new Deletion
            {
                At = _time.GetUtcNow(),
                TrackKey = trackKey,
                FileName = Path.GetFileName(sample),
                Reason = reason,
            }
        );
    }

    public IReadOnlyList<Deletion> All()
    {
        if (_path.Length is 0 || !File.Exists(_path))
            return [];

        List<Deletion> deletions = [];

        try
        {
            using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                try
                {
                    if (
                        JsonSerializer.Deserialize(line, JobsJsonContext.Default.Deletion) is
                        { } deletion
                    )
                        deletions.Add(deletion);
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return deletions;
    }

    private void Append(Deletion deletion)
    {
        if (_path.Length is 0)
            return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(
                    _path,
                    JsonSerializer.Serialize(deletion, JobsJsonContext.Default.Deletion)
                        + Environment.NewLine
                );
            }
        }
        // Losing the record costs a notice, not the deletion it describes.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>One sample crateside recycled.</summary>
public sealed record Deletion
{
    public required DateTimeOffset At { get; init; }

    /// <summary>The upload the file was of. Null for a file that carried no provenance.</summary>
    [JsonPropertyName("videoId")]
    public string? TrackKey { get; init; }

    public required string FileName { get; init; }

    public required string Reason { get; init; }
}
