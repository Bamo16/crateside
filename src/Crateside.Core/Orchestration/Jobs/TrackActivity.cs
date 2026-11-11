using Humanizer;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// One track's history as a single timeline: its stages, what crateside recycled, and what the
/// user did, numbered by pass and with back-to-back repeats folded into one line (#59).
/// </summary>
public static class TrackActivity
{
    public const string You = "you";
    public const string Crateside = "crateside";

    /// <param name="running">Jobs in flight now, whose last "started" line reads as a live "running since" one.</param>
    public static IReadOnlyList<ActivityEntry> Build(
        IEnumerable<JobUpdate> jobs,
        IEnumerable<Deletion> deletions,
        IEnumerable<UserAction> actions,
        IEnumerable<TrackJob>? running = null,
        IEnumerable<StageNote>? notes = null
    )
    {
        var merged = FromJobs([.. jobs], [.. running ?? []])
            .Select(entry => (Entry: entry, StartsPass: false))
            .Concat(deletions.Select(FromDeletion))
            .Concat(actions.Select(FromAction))
            .Concat((notes ?? []).Select(FromNote))
            .OrderBy(entry => entry.Entry.At)
            // A user action and the stage it wakes share a timestamp; the action comes first.
            .ThenBy(entry => entry.Entry.Source == You ? 0 : 1);

        List<ActivityEntry> entries = [];
        var pass = 1;

        foreach (var (entry, startsPass) in merged)
        {
            if (startsPass && entries.Count > 0)
                pass++;

            var numbered = entry with { Pass = pass };

            if (entries is [.., var last] && last with { At = numbered.At, Count = 1 } == numbered)
                entries[^1] = last with { At = numbered.At, Count = last.Count + 1 };
            else
                entries.Add(numbered);
        }

        return entries;
    }

    /// <summary>
    /// A stage that finished with a summary is one line, that summary and how long it took; its
    /// "started" line goes. One still running is a single live line. A line with no summary reads as
    /// it always has.
    /// </summary>
    private static IEnumerable<ActivityEntry> FromJobs(
        IReadOnlyList<JobUpdate> updates,
        IReadOnlyList<TrackJob> running
    )
    {
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            var next = updates.Skip(i + 1).FirstOrDefault(other => other.Id == update.Id);
            var started = updates.Take(i).LastOrDefault(other => other.Id == update.Id)
                is { Status: JobStatus.Running } begun
                ? begun
                : null;

            if (update is { Status: JobStatus.Running } && next is { Summary.Length: > 0 })
                continue;

            var message = update switch
            {
                { Adopted: true } => "picked up a process still running",
                { Status: JobStatus.Running }
                    when next is null
                        && running.Any(job =>
                            job.Id == update.Id && job.UpdatedAt == update.Timestamp
                        ) => $"running since {update.Timestamp.ToLocalTime():HH:mm}",
                { Status: JobStatus.Running } => "started",
                { Summary: { Length: > 0 } summary } when started is not null =>
                    $"{summary} · {Took(update.Timestamp - started.Timestamp)}",
                { Summary: { Length: > 0 } summary } => summary,
                { Status: JobStatus.Held, Error: { Length: > 0 } error, HeldUntil: { } until } =>
                    $"held: {error}; next try {until.ToLocalTime():HH:mm}",
                { Error: { Length: > 0 } error } =>
                    $"{update.Status.Humanize(LetterCasing.LowerCase)}: {error}",
                _ => update.Status.Humanize(LetterCasing.LowerCase),
            };

            yield return new ActivityEntry
            {
                At = update.Timestamp,
                Source = update.Stage.ToString(),
                Message = message,
                IsProblem = update.Status is JobStatus.Failed,
            };
        }
    }

    private static string Took(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{Math.Round(span.TotalMinutes)} min"
            : $"{Math.Round(span.TotalSeconds)} s";

    private static (ActivityEntry Entry, bool StartsPass) FromDeletion(Deletion deletion) =>
        (
            new ActivityEntry
            {
                At = deletion.At,
                Source = Crateside,
                Message = $"recycled {deletion.FileName}: {deletion.Reason}",
            },
            false
        );

    private static (ActivityEntry Entry, bool StartsPass) FromNote(StageNote note) =>
        (
            new ActivityEntry
            {
                At = note.At,
                Source = note.Stage,
                Message = note.Summary,
            },
            false
        );

    private static (ActivityEntry Entry, bool StartsPass) FromAction(UserAction action) =>
        (
            new ActivityEntry
            {
                At = action.At,
                Source = action.By ?? You,
                Message = action.Detail is { Length: > 0 } detail
                    ? $"{action.Kind.Humanize(LetterCasing.LowerCase)}: {detail}"
                    : action.Kind.Humanize(LetterCasing.LowerCase),
            },
            action.PassFrom is not null
        );
}

/// <summary>One line of a track's activity.</summary>
public sealed record ActivityEntry
{
    /// <summary>When it happened, or the last time when <see cref="Count"/> folds several.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>A stage's name, <see cref="TrackActivity.You"/>, or <see cref="TrackActivity.Crateside"/>.</summary>
    public required string Source { get; init; }

    public required string Message { get; init; }

    public int Pass { get; init; } = 1;

    public bool IsProblem { get; init; }

    /// <summary>How many identical lines in a row this one stands for.</summary>
    public int Count { get; init; } = 1;
}
