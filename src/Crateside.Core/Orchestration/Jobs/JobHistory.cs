using System.Globalization;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>What the job record says about the work over time: finished tracks, stage totals, latest events (#78).</summary>
public sealed record JobHistory(
    IReadOnlyList<WeekFinished> Weeks,
    IReadOnlyList<StageTotal> Stages,
    IReadOnlyList<ActivityLine> Latest,
    int Lines,
    DateTimeOffset? Since
)
{
    private const int WeeksShown = 12;
    private const int LatestShown = 8;
    private const string LegacyHoldPrefix = "the judge's endpoint";

    private static readonly JobStage[] Totalled =
    [
        JobStage.Separation,
        JobStage.Verdict,
        JobStage.Handoff,
    ];

    public static JobHistory From(IReadOnlyList<JobUpdate> updates, DateTimeOffset now)
    {
        var finished = updates
            .Where(update => update is { Stage: JobStage.Tagging, Status: JobStatus.Succeeded })
            .GroupBy(update => update.TrackKey, StringComparer.Ordinal)
            .Select(track => WeekOf(track.Min(update => update.Timestamp)))
            .CountBy(week => week)
            .ToDictionary();

        var thisWeek = WeekOf(now);
        var weeks = finished.Count is 0
            ? []
            : Enumerable
                .Range(0, thisWeek.Index - finished.Keys.Min(week => week.Index) + 1)
                .Select(offset => thisWeek.Minus(offset))
                .Reverse()
                .TakeLast(WeeksShown)
                .Select(week => new WeekFinished(
                    week.Number,
                    finished.GetValueOrDefault(week),
                    week == thisWeek
                ))
                .ToList();

        return new JobHistory(
            weeks,
            [
                .. Totalled.Select(stage => new StageTotal(
                    stage,
                    updates.Count(update =>
                        update.Stage == stage && update.Status is JobStatus.Succeeded
                    ),
                    updates.Count(update =>
                        update.Stage == stage && update.Status is JobStatus.Failed
                    ),
                    updates.Count(update =>
                        update.Stage == stage
                        && update.Status is JobStatus.Canceled
                        && !IsHold(update)
                    )
                )),
            ],
            [.. Activity(updates).TakeLast(LatestShown).Reverse()],
            updates.Count,
            updates.Count is 0 ? null : updates.Min(update => update.Timestamp)
        );
    }

    /// <summary>
    /// A judging session that never reached the judge: recorded as held, or by an older crateside
    /// as a canceled judging whose error names the endpoint (#161).
    /// </summary>
    public static bool IsHold(JobUpdate update) =>
        update is { Stage: JobStage.Verdict }
        && (
            update.Status is JobStatus.Held
            || update is { Status: JobStatus.Canceled, Error: { } error }
                && error.StartsWith(LegacyHoldPrefix, StringComparison.Ordinal)
        );

    /// <summary>
    /// The events worth a line: a hold is the stage's, shown once when it begins and once when
    /// judging resumes, not as a started and held pair for each retry.
    /// </summary>
    private static IEnumerable<ActivityLine> Activity(IReadOnlyList<JobUpdate> updates)
    {
        var hidden = StartsOfHeldAttempts(updates);
        string? holding = null;

        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];

            if (update.Status is JobStatus.Queued || hidden.Contains(i))
                continue;

            if (IsHold(update))
            {
                var reason = ReasonOf(update);

                if (reason != holding)
                    yield return new ActivityLine(
                        update.Timestamp,
                        $"held · {Briefly(reason)} · {(update.HeldUntil is { } until ? $"until {until.ToLocalTime():t}" : "trying again shortly")} · next:",
                        update.TrackKey,
                        IsNext: true
                    );

                holding = reason;

                continue;
            }

            if (holding is not null && update.Stage is JobStage.Verdict)
            {
                yield return new ActivityLine(
                    update.Timestamp,
                    "judging resumed",
                    null,
                    IsNext: false
                );
                holding = null;
            }

            yield return new ActivityLine(
                update.Timestamp,
                Describe(update),
                update.TrackKey,
                IsNext: false
            );
        }
    }

    private static HashSet<int> StartsOfHeldAttempts(IReadOnlyList<JobUpdate> updates)
    {
        HashSet<int> starts = [];
        Dictionary<string, int> running = new(StringComparer.Ordinal);

        for (var i = 0; i < updates.Count; i++)
        {
            if (updates[i] is not { Stage: JobStage.Verdict } update)
                continue;

            if (update.Status is JobStatus.Running)
                running[update.TrackKey] = i;
            else if (running.Remove(update.TrackKey, out var started) && IsHold(update))
                starts.Add(started);
        }

        return starts;
    }

    /// <summary>The reason as a feed line says it: "the judge's endpoint refused…" is "endpoint refused…".</summary>
    private static string Briefly(string reason) =>
        reason.Replace("the judge's endpoint", "endpoint", StringComparison.Ordinal);

    private static string ReasonOf(JobUpdate update) =>
        update.Error?.Split("; judging")[0] is { Length: > 0 } reason ? reason : "unavailable";

    /// <summary>A stage change in a few words: <c>separated</c>, <c>judging failed</c>.</summary>
    public static string Describe(JobUpdate update) =>
        (update.Stage, update.Status) switch
        {
            (JobStage.Download, JobStatus.Succeeded) => "downloaded",
            (JobStage.Evidence, JobStatus.Succeeded) => "detected",
            (JobStage.Verdict, JobStatus.Succeeded) => "judged",
            (JobStage.Verdict, JobStatus.AwaitingPerson) => "waiting for you",
            (JobStage.Separation, JobStatus.Succeeded) => "separated",
            (JobStage.Handoff, JobStatus.Succeeded) => "staged",
            (JobStage.Tagging, JobStatus.Succeeded) => "tagged",
            (var stage, JobStatus.Running) => $"{Noun(stage)} started",
            (var stage, JobStatus.Failed) => $"{Noun(stage)} failed",
            (var stage, var status) => $"{Noun(stage)} {status.ToString().ToLowerInvariant()}",
        };

    public static string Noun(JobStage stage) =>
        stage switch
        {
            JobStage.Evidence => "detection",
            JobStage.Verdict => "judging",
            _ => stage.ToString().ToLowerInvariant(),
        };

    private static IsoWeek WeekOf(DateTimeOffset at)
    {
        var local = at.ToLocalTime().DateTime;

        return new IsoWeek(ISOWeek.GetYear(local), ISOWeek.GetWeekOfYear(local));
    }

    private readonly record struct IsoWeek(int Year, int Number)
    {
        /// <summary>Weeks since an arbitrary start, for counting back.</summary>
        public int Index =>
            (int)(ISOWeek.ToDateTime(Year, Number, DayOfWeek.Monday) - DateTime.MinValue).TotalDays
            / 7;

        public IsoWeek Minus(int weeks)
        {
            var monday = ISOWeek.ToDateTime(Year, Number, DayOfWeek.Monday).AddDays(-7 * weeks);

            return new IsoWeek(ISOWeek.GetYear(monday), ISOWeek.GetWeekOfYear(monday));
        }
    }
}

public sealed record WeekFinished(int Week, int Tracks, bool IsCurrent);

public sealed record StageTotal(JobStage Stage, int Done, int Failed, int Canceled);

/// <summary>One line of Latest activity.</summary>
public sealed record ActivityLine(
    DateTimeOffset At,
    string What,
    [property: JsonPropertyName("videoId")] string? TrackKey,
    bool IsNext
);
