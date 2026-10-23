namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// Whether the user has paused separation or judging, kept in <c>%APPDATA%\crateside\paused.json</c>
/// so a pause outlives a restart. Only the user's own pause is kept; both run when nothing is.
/// </summary>
public sealed class PauseStore(string? path = null)
{
    private readonly string _path =
        path ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "paused.json");

    private readonly Lock _gate = new();

    /// <summary>Never throws: an unreadable file reads as nothing paused.</summary>
    public PausedStages Read()
    {
        lock (_gate)
            return PauseJsonContext.Default.PausedStages.ReadFileOrDefault(_path) ?? new();
    }

    public void RememberSeparation(bool paused) => Save(Read() with { Separation = paused });

    public void RememberJudging(bool paused) => Save(Read() with { Judging = paused });

    public void RememberBackfill(bool on) => Save(Read() with { Backfill = on });

    private void Save(PausedStages stages)
    {
        lock (_gate)
        {
            try
            {
                PauseJsonContext.Default.PausedStages.WriteFile(_path, stages);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record PausedStages
{
    public bool Separation { get; init; }

    public bool Judging { get; init; }

    /// <summary>Not a pause: whether the weekly spend-down judges the backlog (ADR 0063).</summary>
    public bool Backfill { get; init; }
}
