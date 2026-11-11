using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// Everything the user did to a track, in <c>%APPDATA%\crateside\actions.jsonl</c>: the half of a
/// track's activity that <c>jobs.jsonl</c> cannot say (#59).
/// </summary>
public sealed class ActionLog(CratesidePaths paths, TimeProvider? time = null)
{
    private readonly string _path = paths.ActionsFile;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    internal ActionLog(string path, TimeProvider? time = null)
        : this(new CratesidePaths { ActionsFile = path }, time) { }

    /// <summary>Records nothing, for a caller with nowhere to say it.</summary>
    public static ActionLog None { get; } = new(string.Empty);

    /// <param name="passFrom">The stage this action sends the track back to, if it starts a pass.</param>
    /// <param name="by">Who asked through the API; null for the person at the pages.</param>
    public void Record(
        string trackKey,
        UserActionKind kind,
        string? detail = null,
        JobStage? passFrom = null,
        string? by = null
    ) =>
        Append(
            new UserAction
            {
                At = _time.GetUtcNow(),
                TrackKey = trackKey,
                Kind = kind,
                Detail = detail,
                PassFrom = passFrom,
                By = by,
            }
        );

    /// <summary>One track's actions, oldest first.</summary>
    public IReadOnlyList<UserAction> For(string trackKey) =>
        [.. All().Where(action => action.TrackKey.Equals(trackKey, StringComparison.Ordinal))];

    public IReadOnlyList<UserAction> All()
    {
        if (_path.Length is 0 || !File.Exists(_path))
            return [];

        List<UserAction> actions = [];

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
                        JsonSerializer.Deserialize(line, JobsJsonContext.Default.UserAction) is
                        { } action
                    )
                        actions.Add(action);
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return actions;
    }

    private void Append(UserAction action)
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
                    JsonSerializer.Serialize(action, JobsJsonContext.Default.UserAction)
                        + Environment.NewLine
                );
            }
        }
        // Losing the record costs a line of history, not the action it describes.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>One thing the user did to a track.</summary>
public sealed record UserAction
{
    public required DateTimeOffset At { get; init; }

    [JsonPropertyName("videoId")]
    public required string TrackKey { get; init; }

    public required UserActionKind Kind { get; init; }

    /// <summary>What it was done with: the new name, the genre, the upload, the files cut.</summary>
    public string? Detail { get; init; }

    /// <summary>The stage the track went back to, when the action started a pass.</summary>
    public JobStage? PassFrom { get; init; }

    /// <summary>Who asked, when it came through the API; null for the person at the pages.</summary>
    public string? By { get; init; }
}

public enum UserActionKind
{
    Added,
    Renamed,
    RenameReverted,
    Swapped,
    GenreSettled,
    FamiliaritySettled,
    MarkSet,
    MarkCleared,
    SameSong,
    DifferentSong,
    UsedAnyway,
    PlanChanged,
    Recut,
    ShortfallCut,
    SourceAdded,
    SampleAdopted,
    Retried,
    Bumped,
    BumpCleared,
    RemovedFromPlaylist,
    Forgotten,
    ArrivedAlreadyFiled,
    PassedTheHold,
    PassCleared,
    KeyChosen,
    PageRejected,
    PageCounted,
    PageAdded,
    PageChoiceUndone,
    SourceRemoved,
    SourceRestored,
    Rejudged,
}
