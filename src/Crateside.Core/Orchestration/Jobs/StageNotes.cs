using System.Text.Json;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// One line on what a pass with no job record did to a song, in <c>%APPDATA%\crateside\stage-notes.jsonl</c>:
/// the warp pass and loudness, which the activity feed merges in beside the job lines (#170, ADR 0073).
/// </summary>
public sealed class StageNotes(string? path = null, TimeProvider? time = null)
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "stage-notes.jsonl"
        );
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    public void Record(string videoId, string stage, string summary) =>
        Append(
            new StageNote
            {
                At = _time.GetUtcNow(),
                VideoId = videoId,
                Stage = stage,
                Summary = summary,
            }
        );

    /// <summary>The notes of any of these uploads, oldest first.</summary>
    public IReadOnlyList<StageNote> For(IReadOnlySet<string> videoIds) =>
        [.. All().Where(note => videoIds.Contains(note.VideoId))];

    public IReadOnlyList<StageNote> All()
    {
        if (!File.Exists(_path))
            return [];

        List<StageNote> notes = [];

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
                        JsonSerializer.Deserialize(line, JobsJsonContext.Default.StageNote) is
                        { } note
                    )
                        notes.Add(note);
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return notes;
    }

    private void Append(StageNote note)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(
                    _path,
                    JsonSerializer.Serialize(note, JobsJsonContext.Default.StageNote)
                        + Environment.NewLine
                );
            }
        }
        // Losing the note costs a line of history, not the pass it describes.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>What one pass did to one song, in about eighty characters.</summary>
public sealed record StageNote
{
    public required DateTimeOffset At { get; init; }

    public required string VideoId { get; init; }

    /// <summary>The pass's name as the activity feed shows it: <c>Warp</c> or <c>Loudness</c>.</summary>
    public required string Stage { get; init; }

    public required string Summary { get; init; }
}
