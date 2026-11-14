using Humanizer;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// One track's history in the dashboard's row model: its jobs as <see cref="ActivityFeed"/> makes
/// them, with what crateside recycled, what the user did and the passes' notes among them as rows
/// of their own, newest first (#59).
/// </summary>
public static class TrackActivity
{
    public const string You = "you";
    public const string Crateside = "crateside";

    public static IReadOnlyList<ActivityAction> Build(
        IEnumerable<JobUpdate> jobs,
        IEnumerable<Deletion> deletions,
        IEnumerable<UserAction> actions,
        IEnumerable<StageNote>? notes = null
    )
    {
        var fromJobs = ActivityFeed.From([.. jobs]).Actions;
        var next = fromJobs.Count is 0 ? 0 : fromJobs.Max(action => action.Sequence) + 1;

        return
        [
            .. fromJobs
                .Concat(
                    deletions
                        .Select(deletion =>
                            Row(
                                deletion.TrackKey ?? string.Empty,
                                deletion.At,
                                Crateside,
                                $"recycled {deletion.FileName}: {deletion.Reason}"
                            )
                        )
                        .Concat(
                            actions.Select(action =>
                                Row(action.TrackKey, action.At, action.By ?? You, Describe(action))
                            )
                        )
                        .Concat(
                            (notes ?? []).Select(note =>
                                Row(note.TrackKey, note.At, note.Stage, note.Summary)
                            )
                        )
                        .Select((action, i) => action with { Sequence = next + i })
                )
                .OrderByDescending(action => action.StartedAt)
                // A user action and the stage it wakes share a timestamp; the stage reads above it.
                .ThenBy(action => action.Source is You ? 1 : 0)
                .ThenByDescending(action => action.Sequence),
        ];
    }

    /// <summary>Whether a row is the person's own doing.</summary>
    public static bool IsYou(ActivityAction action) => action.Source is You;

    /// <summary>One row of the copied log: when, who or which stage, what it said, how long it took.</summary>
    public static string CopyLine(
        ActivityAction action,
        DateTimeOffset now,
        Func<DateTimeOffset, string> stamp
    ) =>
        $"{stamp(action.StartedAt)}  {action.Label, -10}  {action.FullText}{(action.Took(now) is { } took ? $" · {ActivityFeed.Duration(took)}" : null)}";

    private static string Describe(UserAction action) =>
        action.Detail is { Length: > 0 } detail
            ? $"{action.Kind.Humanize(LetterCasing.LowerCase)}: {detail}"
            : action.Kind.Humanize(LetterCasing.LowerCase);

    private static ActivityAction Row(
        string trackKey,
        DateTimeOffset at,
        string source,
        string text
    ) =>
        new()
        {
            Sequence = 0,
            Id = string.Empty,
            TrackKey = trackKey,
            StartedAt = at,
            HasStart = false,
            Source = source,
            State = ActivityState.Done,
            Text = text,
        };
}
