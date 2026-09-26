using System.Text.Json;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// Whether the user has paused separation or judging, kept in <c>%APPDATA%\crateside\paused.json</c>
/// so a pause outlives a restart. Only the user's own pause is kept; both run when nothing is.
/// </summary>
public sealed class PauseStore(string? path = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path =
        path ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "paused.json");

    private readonly Lock _gate = new();

    /// <summary>Never throws: an unreadable file reads as nothing paused.</summary>
    public PausedStages Read()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_path)
                    ? JsonSerializer.Deserialize<PausedStages>(File.ReadAllText(_path)) ?? new()
                    : new();
            }
            catch (Exception ex)
                when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return new();
            }
        }
    }

    public void RememberSeparation(bool paused) => Save(Read() with { Separation = paused });

    public void RememberJudging(bool paused) => Save(Read() with { Judging = paused });

    public void RememberBackfill(bool on) => Save(Read() with { Backfill = on });

    public void RememberBackfillJudged(DateTimeOffset at) =>
        Save(Read() with { BackfillJudgedAt = at });

    private void Save(PausedStages stages)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(stages, SerializerOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record PausedStages
{
    public bool Separation { get; init; }

    public bool Judging { get; init; }

    /// <summary>Not a pause: whether idle windows judge the library backfill (ADR 0035).</summary>
    public bool Backfill { get; init; }

    /// <summary>When an idle window last judged a backfill track, so a restart keeps the five hours.</summary>
    public DateTimeOffset? BackfillJudgedAt { get; init; }
}
