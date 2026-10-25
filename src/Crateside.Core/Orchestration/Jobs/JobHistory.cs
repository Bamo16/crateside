using System.Globalization;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>What the job record says about the work over time: finished tracks, stage totals, latest events (#78).</summary>
public sealed record JobHistory(
    IReadOnlyList<WeekFinished> Weeks,
    IReadOnlyList<StageTotal> Stages,
    IReadOnlyList<JobUpdate> Latest,
    int Lines,
    DateTimeOffset? Since
)
{
    private const int WeeksShown = 12;
    private const int LatestShown = 8;

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
            .GroupBy(update => update.VideoId, StringComparer.Ordinal)
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
                        update.Stage == stage && update.Status is JobStatus.Canceled
                    )
                )),
            ],
            [
                .. updates
                    .Where(update => update.Status is not JobStatus.Queued)
                    .TakeLast(LatestShown)
                    .Reverse(),
            ],
            updates.Count,
            updates.Count is 0 ? null : updates.Min(update => update.Timestamp)
        );
    }

    /// <summary>A stage change in a few words: <c>separated</c>, <c>judging failed</c>.</summary>
    public static string Describe(JobUpdate update) =>
        (update.Stage, update.Status) switch
        {
            (JobStage.Download, JobStatus.Succeeded) => "downloaded",
            (JobStage.Evidence, JobStatus.Succeeded) => "detected",
            (JobStage.Verdict, JobStatus.Succeeded) => "judged",
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
