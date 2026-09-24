using System.Text.Json;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Every sample crateside sent to the recycle bin on its own initiative, in
/// <c>%APPDATA%\crateside\deletions.jsonl</c>.
/// </summary>
public sealed class DeletionLog(string path, TimeProvider? time = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    /// <summary>Recycles without recording, for a caller with nowhere to say it.</summary>
    public static DeletionLog None { get; } = new(string.Empty);

    public static DeletionLog Default() =>
        new(
            Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "deletions.jsonl")
        );

    /// <summary>
    /// Sends a sample to the recycle bin and records which upload it was of — read before it goes
    /// — and why.
    /// </summary>
    public void Recycle(string sample, string reason)
    {
        if (!File.Exists(sample))
            return;

        var videoId = StemTagReader.Read(sample)?.SourceVideoId;

        RecycleBin.Remove(sample);

        Append(
            new Deletion
            {
                At = _time.GetUtcNow(),
                VideoId = videoId,
                FileName = Path.GetFileName(sample),
                Reason = reason,
            }
        );
    }

    public IReadOnlyList<Deletion> All()
    {
        if (path.Length is 0 || !File.Exists(path))
            return [];

        List<Deletion> deletions = [];

        try
        {
            using var stream = new FileStream(
                path,
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
                        JsonSerializer.Deserialize<Deletion>(line, SerializerOptions) is
                        { } deletion
                    )
                        deletions.Add(deletion);
                }
                catch (JsonException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return deletions;
    }

    private void Append(Deletion deletion)
    {
        if (path.Length is 0)
            return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(
                    path,
                    JsonSerializer.Serialize(deletion, SerializerOptions) + Environment.NewLine
                );
            }
        }
        // Losing the record costs a notice, not the deletion it describes.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>One sample crateside recycled.</summary>
public sealed record Deletion
{
    public required DateTimeOffset At { get; init; }

    /// <summary>The upload the file was of. Null for a file that carried no provenance.</summary>
    public string? VideoId { get; init; }

    public required string FileName { get; init; }

    public required string Reason { get; init; }
}
