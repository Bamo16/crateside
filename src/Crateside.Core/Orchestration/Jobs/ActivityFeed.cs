using Humanizer;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// The dashboard's Activity section: <c>jobs.jsonl</c> as one entry per action, a stage's start
/// and its finish joined on the job id, newest start first (#189).
/// </summary>
public sealed record ActivityFeed(IReadOnlyList<ActivityAction> Actions)
{
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(10);

    public static readonly int[] PageSizes = [12, 25, 50, 100];

    /// <summary>The stages in the order the filter pills show them, with the names the person knows them by.</summary>
    public static readonly (JobStage Stage, string Label)[] StageLabels =
    [
        (JobStage.Download, "Download"),
        (JobStage.Evidence, "Detection"),
        (JobStage.Verdict, "Judging"),
        (JobStage.Separation, "Separation"),
        (JobStage.Handoff, "Handoff"),
        (JobStage.Tagging, "Tagging"),
    ];

    public int Running => Actions.Count(action => action.IsRunning);

    public int FailedOrCanceled => Actions.Count(action => action.IsFailedOrCanceled);

    /// <summary>The latest thing to happen: a start or a finish.</summary>
    public DateTimeOffset? LastAt =>
        Actions.Count is 0 ? null : Actions.Max(action => action.FinishedAt ?? action.StartedAt);

    public ActivityAction? LastFinished =>
        Actions
            .Where(action => action is { State: ActivityState.Done, FinishedAt: not null })
            .MaxBy(action => action.FinishedAt);

    public static ActivityFeed From(IReadOnlyList<JobUpdate> updates)
    {
        List<ActivityAction> actions = [];
        Dictionary<string, (int Sequence, JobUpdate Start)> open = new(StringComparer.Ordinal);
        Dictionary<string, ActivityAction> last = new(StringComparer.Ordinal);

        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];

            if (update.Status is JobStatus.Queued)
                continue;

            if (update.Status is JobStatus.Running)
            {
                // A restart adopting a process keeps the start it already has.
                if (update.Adopted is not true || !open.ContainsKey(update.Id))
                    open[update.Id] = (i, update);

                continue;
            }

            var action = open.Remove(update.Id, out var begun)
                ? Finish(begun.Sequence, begun.Start, update)
                : Finish(i, null, update);

            // A held judging retries every few minutes with the same reason; once is enough.
            if (
                action is { State: ActivityState.Held }
                && last.TryGetValue(update.Id, out var before)
                && before is { State: ActivityState.Held } held
                && held.Text == action.Text
            )
                continue;

            last[update.Id] = action;
            actions.Add(action);
        }

        actions.AddRange(
            open.Values.Select(running => new ActivityAction
            {
                Sequence = running.Sequence,
                Id = running.Start.Id,
                TrackKey = running.Start.TrackKey,
                Stage = running.Start.Stage,
                StartedAt = running.Start.Timestamp,
                State = ActivityState.Running,
                Text = "running",
            })
        );

        return new([.. actions.OrderByDescending(action => action.Sequence)]);
    }

    public static string StageName(JobStage stage) =>
        StageLabels.FirstOrDefault(entry => entry.Stage == stage).Label ?? stage.ToString();

    /// <summary>A duration as the table shows it: <c>3 min 21 s</c>, <c>14 s</c>, <c>&lt;1 s</c>.</summary>
    public static string Duration(TimeSpan span) =>
        span.TotalSeconds switch
        {
            < 1 => "<1 s",
            < 60 => $"{(int)span.TotalSeconds} s",
            _ when span.Seconds is 0 && span.Hours is 0 => $"{(int)span.TotalMinutes} min",
            _ when span.TotalHours < 1 => $"{(int)span.TotalMinutes} min {span.Seconds} s",
            _ => $"{(int)span.TotalHours} h {span.Minutes} min",
        };

    public int FinishedRecently(DateTimeOffset now) =>
        Actions.Count(action => action is { FinishedAt: { } finished } && now - finished < Recent);

    /// <summary>Everything on record at a stage, or at all stages for <c>null</c>: the filter pills' counts.</summary>
    public int Count(JobStage? stage) =>
        stage is { } only ? Actions.Count(action => action.Stage == only) : Actions.Count;

    public int FailedSince(DateTimeOffset since) =>
        Actions.Count(action =>
            action is { State: ActivityState.Failed, FinishedAt: { } at } && at >= since
        );

    /// <summary>The actions a query matches, newest start first, none from after its anchor.</summary>
    public IEnumerable<ActivityAction> Matching(ActivityQuery query) =>
        Actions.Where(action =>
            (query.Stages.Count is 0 || action.Stage is { } stage && query.Stages.Contains(stage))
            && (!query.FailedOnly || action.IsFailedOrCanceled)
            && (query.AnchorSequence is not { } anchor || action.Sequence <= anchor)
        );

    /// <summary>How many matching actions started after the reader paged away from page 1.</summary>
    public int NewSince(ActivityQuery query) =>
        query.AnchorSequence is { } anchor
            ? Matching(query with { AnchorSequence = null })
                .Count(action => action.Sequence > anchor)
            : 0;

    public ActivityPage PageOf(ActivityQuery query)
    {
        var matching = Matching(query).ToList();
        var pages = Math.Max(1, (matching.Count + query.PageSize - 1) / query.PageSize);
        var page = Math.Clamp(query.Page, 1, pages);
        var skipped = (page - 1) * query.PageSize;

        return new ActivityPage(
            [.. matching.Skip(skipped).Take(query.PageSize)],
            matching.Count,
            page,
            pages,
            skipped + 1
        );
    }

    /// <summary>The page links: the first three, the last, and the current one with its neighbors; <c>null</c> is a gap.</summary>
    public static IReadOnlyList<int?> PageLinks(int page, int pages)
    {
        List<int?> links = [];

        foreach (
            var number in Enumerable
                .Range(1, Math.Min(3, pages))
                .Concat([pages, page - 1, page, page + 1])
                .Where(number => number >= 1 && number <= pages)
                .Distinct()
                .Order()
        )
        {
            if (links is [.., { } previous] && number - previous > 1)
                links.Add(null);

            links.Add(number);
        }

        return links;
    }

    /// <summary>The bucket a start falls in, with the label and the range its divider shows.</summary>
    public static ActivityBucket BucketOf(DateTimeOffset startedAt, DateTimeOffset now)
    {
        var local = startedAt.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var cutoff = (now - Recent).ToLocalTime();

        return startedAt >= now - Recent ? new("Last 10 minutes", $"since {cutoff:t}")
            : local.Date == today ? new("Earlier today", $"before {cutoff:t}")
            : local.Date == today.AddDays(-1) ? new("Yesterday", $"{local:ddd d MMM}")
            : new($"{local:ddd d MMM}", string.Empty);
    }

    private static ActivityAction Finish(int sequence, JobUpdate? start, JobUpdate end)
    {
        var state = end.Status switch
        {
            JobStatus.Failed => ActivityState.Failed,
            JobStatus.Canceled when JobHistory.IsHold(end) => ActivityState.Held,
            JobStatus.Canceled => ActivityState.Canceled,
            JobStatus.Held or JobStatus.AwaitingPerson => ActivityState.Held,
            _ => ActivityState.Done,
        };

        // The wording Track details' activity log shows for the same record.
        var text = end switch
        {
            { Status: JobStatus.AwaitingPerson } => "waiting for you",
            { Status: JobStatus.Held, Error: { Length: > 0 } error } => $"held: {error}",
            { SummaryText: { Length: > 0 } summary } => summary,
            { Error: { Length: > 0 } error } => error,
            // A line written before summaries (#170) has nothing to say but that it finished.
            { Status: JobStatus.Succeeded } => "finished, no details recorded",
            _ => end.Status.Humanize(LetterCasing.LowerCase),
        };

        return new ActivityAction
        {
            Sequence = sequence,
            Id = end.Id,
            TrackKey = end.TrackKey,
            Stage = end.Stage,
            StartedAt = start?.Timestamp ?? end.Timestamp,
            FinishedAt = end.Timestamp,
            HasStart = start is not null,
            State = state,
            Text = text,
        };
    }
}

public enum ActivityState
{
    Running,
    Done,
    Failed,
    Canceled,

    /// <summary>Held by the judge's endpoint, or waiting for the person; drawn like a cancellation.</summary>
    Held,
}

public sealed record ActivityAction
{
    /// <summary>Where the start sits in the file: orders by start, and never moves.</summary>
    public required int Sequence { get; init; }

    /// <summary>The job's id; empty for a row that is not a job.</summary>
    public required string Id { get; init; }
    public required string TrackKey { get; init; }

    /// <summary>The stage, or null for a row that is not a job: something the person did, a recycled file, a pass's note.</summary>
    public JobStage? Stage { get; init; }

    /// <summary>Who a row that is not a job is from: <c>you</c>, <c>crateside</c>, an API caller or a pass's name.</summary>
    public string? Source { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>False for a finish whose start was never recorded, so it has no duration.</summary>
    public bool HasStart { get; init; } = true;
    public required ActivityState State { get; init; }

    /// <summary>The summary, or for a failure its error, without the "failed: " lead the row adds.</summary>
    public required string Text { get; init; }

    /// <summary>What the Stage column says: the stage's name, or who a row that is not a job is from.</summary>
    public string Label =>
        Stage is { } stage ? ActivityFeed.StageName(stage) : Source ?? string.Empty;

    /// <summary>The lead a failure or cancellation puts before its text, so it reads as one in plain text too.</summary>
    public string Lead =>
        State switch
        {
            ActivityState.Failed => "failed: ",
            ActivityState.Canceled => "canceled: ",
            _ => string.Empty,
        };

    public string FullText => Lead + Text;

    public bool IsRunning => State is ActivityState.Running;

    public bool IsFailedOrCanceled => State is ActivityState.Failed or ActivityState.Canceled;

    public TimeSpan? Took(DateTimeOffset now) =>
        IsRunning ? now - StartedAt
        : HasStart && FinishedAt is { } finished ? finished - StartedAt
        : null;
}

public sealed record ActivityBucket(string Label, string Range);

/// <summary>What the section shows. The stages, the failed toggle and the page size are remembered; the page is not.</summary>
public sealed record ActivityQuery
{
    public IReadOnlySet<JobStage> Stages { get; init; } = new HashSet<JobStage>();
    public bool FailedOnly { get; init; }
    public int PageSize { get; init; } = ActivityFeed.PageSizes[0];
    public int Page { get; init; } = 1;

    /// <summary>The newest start when the reader left page 1; later starts wait behind a line.</summary>
    public int? AnchorSequence { get; init; }

    /// <summary>The remembered part, as <c>Download:Evidence|1|25</c>: stages, failed toggle, page size.</summary>
    public string ToCookie() =>
        $"{string.Join(':', Stages.Order())}|{(FailedOnly ? 1 : 0)}|{PageSize}";

    /// <summary>The query a cookie remembers; anything unreadable falls back to the defaults.</summary>
    public static ActivityQuery FromCookie(string? cookie) =>
        cookie?.Split('|') is [var stages, var failed, var size]
            ? new ActivityQuery
            {
                Stages = stages
                    .Split(':', StringSplitOptions.RemoveEmptyEntries)
                    .Select(name =>
                        Enum.TryParse<JobStage>(name, out var stage) ? stage : (JobStage?)null
                    )
                    .OfType<JobStage>()
                    .ToHashSet(),
                FailedOnly = failed is "1",
                PageSize =
                    int.TryParse(size, out var parsed) && ActivityFeed.PageSizes.Contains(parsed)
                        ? parsed
                        : ActivityFeed.PageSizes[0],
            }
            : new ActivityQuery();
}

public sealed record ActivityPage(
    IReadOnlyList<ActivityAction> Actions,
    int Total,
    int Page,
    int Pages,
    int First
);
