using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// Whether the user has paused separation or judging, kept in <c>%APPDATA%\crateside\paused.json</c>
/// so a pause outlives a restart. Only the user's own pause is kept; both run when nothing is.
/// </summary>
public sealed class PauseStore(CratesidePaths paths)
{
    private readonly string _path = paths.PausedFile;

    private readonly Lock _gate = new();

    internal PauseStore(string path)
        : this(new CratesidePaths { PausedFile = path }) { }

    /// <summary>Never throws: an unreadable file reads as nothing paused.</summary>
    public PausedStages Read()
    {
        lock (_gate)
            return PauseJsonContext.Default.PausedStages.ReadFileOrDefault(_path) ?? new();
    }

    public void RememberSeparation(bool paused) => Save(Read() with { Separation = paused });

    public void RememberJudging(bool paused) => Save(Read() with { Judging = paused });

    public void RememberBackfill(bool on) => Save(Read() with { Backfill = on });

    /// <summary>Not a pause: the judge's refusals in a row and the hold they set (ADR 0070), or null once a session is served.</summary>
    public void RememberBackoff(JudgeBackoffState? backoff) =>
        Save(Read() with { Backoff = backoff });

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

    /// <summary>Not a pause: the judge backoff, kept so a restart does not reset it (ADR 0070).</summary>
    public JudgeBackoffState? Backoff { get; init; }
}
