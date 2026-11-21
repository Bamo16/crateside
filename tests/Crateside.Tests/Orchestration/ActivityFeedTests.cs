using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration;

public sealed class ActivityFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 14, 14, 0, TimeSpan.Zero);

    [Fact]
    public void A_start_and_its_finish_are_one_action_that_keeps_the_start_time_and_the_summary()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Separation, JobStatus.Running, -10),
            Update("a", JobStage.Separation, JobStatus.Succeeded, -7, summary: "cut 3 files"),
        ]);

        var action = Assert.Single(feed.Actions);

        Assert.Equal(ActivityState.Done, action.State);
        Assert.Equal("cut 3 files", action.Text);
        Assert.Equal(Now.AddMinutes(-10), action.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(3), action.Took(Now));
    }

    [Fact]
    public void A_stage_still_running_is_an_action_whose_duration_is_the_time_so_far()
    {
        var feed = ActivityFeed.From([Update("a", JobStage.Verdict, JobStatus.Running, -2)]);

        var action = Assert.Single(feed.Actions);

        Assert.True(action.IsRunning);
        Assert.Equal("running", action.Text);
        Assert.Equal(TimeSpan.FromMinutes(2), action.Took(Now));
        Assert.Equal(1, feed.Running);
    }

    [Fact]
    public void A_retry_of_the_same_job_is_a_second_action_not_a_rewrite_of_the_first()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Download, JobStatus.Running, -9),
            Update("a", JobStage.Download, JobStatus.Failed, -8, error: "offline"),
            Update("a", JobStage.Download, JobStatus.Running, -5),
            Update("a", JobStage.Download, JobStatus.Succeeded, -4, summary: "downloaded"),
        ]);

        Assert.Equal(
            [ActivityState.Done, ActivityState.Failed],
            feed.Actions.Select(action => action.State)
        );
        Assert.Equal("offline", feed.Actions[1].Text);
    }

    [Fact]
    public void Actions_are_ordered_by_when_they_started_not_when_they_finished()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Separation, JobStatus.Running, -20),
            Update("b", JobStage.Download, JobStatus.Running, -15),
            Update("b", JobStage.Download, JobStatus.Succeeded, -14),
            Update("a", JobStage.Separation, JobStatus.Succeeded, -1),
        ]);

        Assert.Equal(["b", "a"], feed.Actions.Select(action => action.TrackKey));
    }

    [Fact]
    public void A_restart_adopting_a_process_keeps_the_original_start()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Separation, JobStatus.Running, -30),
            Update("a", JobStage.Separation, JobStatus.Running, -10, adopted: true),
            Update("a", JobStage.Separation, JobStatus.Succeeded, -5),
        ]);

        Assert.Equal(Now.AddMinutes(-30), Assert.Single(feed.Actions).StartedAt);
    }

    [Fact]
    public void A_finish_with_no_recorded_start_has_no_duration_and_says_it_finished()
    {
        var feed = ActivityFeed.From([Update("a", JobStage.Tagging, JobStatus.Succeeded, -3)]);

        var action = Assert.Single(feed.Actions);

        Assert.Null(action.Took(Now));
        Assert.Equal("finished, no details recorded", action.Text);
    }

    [Fact]
    public void A_queued_line_is_not_an_action()
    {
        var feed = ActivityFeed.From([Update("a", JobStage.Download, JobStatus.Queued, -3)]);

        Assert.Empty(feed.Actions);
    }

    [Fact]
    public void A_held_judging_that_repeats_its_reason_is_listed_once()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Verdict, JobStatus.Running, -9),
            Update("a", JobStage.Verdict, JobStatus.Held, -9, error: "rate limited"),
            Update("a", JobStage.Verdict, JobStatus.Running, -6),
            Update("a", JobStage.Verdict, JobStatus.Held, -6, error: "rate limited"),
        ]);

        var action = Assert.Single(feed.Actions);

        Assert.Equal(ActivityState.Held, action.State);
        Assert.Equal("held: rate limited", action.Text);
        Assert.False(action.IsFailedOrCanceled);
    }

    [Fact]
    public void The_failed_or_canceled_filter_keeps_failures_and_cancellations_only()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Download, JobStatus.Succeeded, -9),
            Update("b", JobStage.Download, JobStatus.Failed, -8, error: "offline"),
            Update("c", JobStage.Handoff, JobStatus.Canceled, -7, error: "by hand"),
            Update("d", JobStage.Verdict, JobStatus.Held, -6, error: "limit"),
        ]);

        Assert.Equal(
            ["c", "b"],
            feed.Matching(new ActivityQuery { FailedOnly = true }).Select(a => a.TrackKey)
        );
        Assert.Equal(2, feed.FailedOrCanceled);
    }

    [Fact]
    public void Stages_combine_and_none_means_all()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Download, JobStatus.Succeeded, -9),
            Update("b", JobStage.Evidence, JobStatus.Succeeded, -8),
            Update("c", JobStage.Verdict, JobStatus.Succeeded, -7),
        ]);

        Assert.Equal(
            ["b", "a"],
            feed.Matching(
                    new ActivityQuery
                    {
                        Stages = new HashSet<JobStage> { JobStage.Download, JobStage.Evidence },
                    }
                )
                .Select(a => a.TrackKey)
        );
        Assert.Equal(3, feed.Matching(new ActivityQuery()).Count());
        Assert.Equal(1, feed.Count(JobStage.Verdict));
        Assert.Equal(3, feed.Count(null));
    }

    [Fact]
    public void Paging_is_newest_first_and_a_page_past_the_end_is_the_last_one()
    {
        var feed = ActivityFeed.From([
            .. Enumerable
                .Range(0, 30)
                .Select(i => Update($"t{i}", JobStage.Download, JobStatus.Succeeded, -40 + i)),
        ]);

        var second = feed.PageOf(new ActivityQuery { Page = 2 });
        var past = feed.PageOf(new ActivityQuery { Page = 9 });

        Assert.Equal(12, second.Actions.Count);
        Assert.Equal("t17", second.Actions[0].TrackKey);
        Assert.Equal(13, second.First);
        Assert.Equal(3, second.Pages);
        Assert.Equal(30, second.Total);
        Assert.Equal(3, past.Page);
        Assert.Equal(6, past.Actions.Count);
    }

    [Fact]
    public void A_page_that_was_left_holds_still_and_counts_what_started_since()
    {
        List<JobUpdate> updates =
        [
            .. Enumerable
                .Range(0, 30)
                .Select(i => Update($"t{i}", JobStage.Download, JobStatus.Succeeded, -40 + i)),
        ];
        var before = ActivityFeed.From(updates);
        var query = new ActivityQuery { Page = 2, AnchorSequence = before.Actions[0].Sequence };

        updates.Add(Update("new1", JobStage.Evidence, JobStatus.Running, -1));
        updates.Add(Update("new2", JobStage.Evidence, JobStatus.Running, 0));
        var after = ActivityFeed.From(updates);

        Assert.Equal(
            before.PageOf(query).Actions.Select(action => action.TrackKey),
            after.PageOf(query).Actions.Select(action => action.TrackKey)
        );
        Assert.Equal(2, after.NewSince(query));
        Assert.Equal(0, after.NewSince(query with { AnchorSequence = null }));
    }

    [Theory]
    [InlineData(1, 1, new[] { 1 })]
    [InlineData(1, 7, new[] { 1, 2, 3, -1, 7 })]
    [InlineData(4, 7, new[] { 1, 2, 3, 4, 5, -1, 7 })]
    [InlineData(6, 7, new[] { 1, 2, 3, -1, 5, 6, 7 })]
    [InlineData(10, 20, new[] { 1, 2, 3, -1, 9, 10, 11, -1, 20 })]
    public void Page_links_show_the_ends_and_the_current_page_with_gaps_between(
        int page,
        int pages,
        int[] expected
    ) =>
        Assert.Equal(
            expected.Select(number => number is -1 ? (int?)null : number),
            ActivityFeed.PageLinks(page, pages)
        );

    [Fact]
    public void An_action_belongs_to_the_bucket_of_its_start()
    {
        Assert.Equal("Last 10 minutes", Bucket(-5).Label);
        Assert.Equal("Earlier today", Bucket(-30).Label);
        Assert.Equal("Yesterday", Bucket(-60 * 24).Label);
        Assert.Equal("since " + Now.AddMinutes(-10).ToLocalTime().ToString("t"), Bucket(-5).Range);
        Assert.Matches(@"^\w{3} \d{1,2} \w{3}$", Bucket(-60 * 24 * 3).Label);
    }

    [Fact]
    public void The_gist_figures_count_what_is_running_and_what_finished_recently()
    {
        var feed = ActivityFeed.From([
            Update("a", JobStage.Download, JobStatus.Succeeded, -30),
            Update("b", JobStage.Download, JobStatus.Succeeded, -4),
            Update("c", JobStage.Evidence, JobStatus.Failed, -2, error: "x"),
            Update("d", JobStage.Verdict, JobStatus.Running, -1),
        ]);

        Assert.Equal(1, feed.Running);
        Assert.Equal(2, feed.FinishedRecently(Now));
        Assert.Equal(Now.AddMinutes(-1), feed.LastAt);
        Assert.Equal("b", feed.LastFinished?.TrackKey);
        Assert.Equal(1, feed.FailedSince(Now.AddHours(-1)));
    }

    [Fact]
    public void The_remembered_filters_survive_a_round_trip_through_the_cookie()
    {
        var query = new ActivityQuery
        {
            Stages = new HashSet<JobStage> { JobStage.Verdict, JobStage.Download },
            FailedOnly = true,
            PageSize = 50,
        };

        var back = ActivityQuery.FromCookie(query.ToCookie());

        Assert.Equal("Download:Verdict|1|50", query.ToCookie());
        Assert.Equal(query.Stages.Order(), back.Stages.Order());
        Assert.True(back.FailedOnly);
        Assert.Equal(50, back.PageSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("Nope|1|7")]
    public void An_unreadable_cookie_gives_the_defaults(string? cookie)
    {
        var query = ActivityQuery.FromCookie(cookie);

        Assert.Empty(query.Stages);
        Assert.Equal(12, query.PageSize);
    }

    private static ActivityBucket Bucket(int minutes) =>
        ActivityFeed.BucketOf(Now.AddMinutes(minutes), Now);

    private static JobUpdate Update(
        string trackKey,
        JobStage stage,
        JobStatus status,
        int minutes,
        string? error = null,
        string? summary = null,
        bool adopted = false
    ) =>
        new()
        {
            Id = $"{trackKey}:{stage}",
            TrackKey = trackKey,
            Stage = stage,
            Status = status,
            Timestamp = Now.AddMinutes(minutes),
            Error = error,
            Summary = summary,
            Adopted = adopted ? true : null,
        };
}
