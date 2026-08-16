using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

public class SeparationSelectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>A ledger that has never heard of any of these tracks.</summary>
    private static readonly KeyLedger NoLedger = KeyLedger.Read("does-not-exist");

    /// <summary>A ledger holding a verdict for each of these tracks — what makes them ready.</summary>
    private KeyLedger Judged(params string[] videoIds) => JudgedAs("HIGH", videoIds);

    /// <summary>
    /// The same, at a stated confidence. Deliberately <em>not</em> an overload of
    /// <see cref="Judged"/>: with a trailing <c>params</c> both are applicable in expanded form,
    /// so <c>Judged("someVideoId")</c> silently binds the id as the confidence and writes an
    /// empty ledger — which is a passing-looking test that exercises nothing.
    /// </summary>
    private KeyLedger JudgedAs(string confidence, params string[] videoIds)
    {
        var path = Path.Combine(_directory, "runs.jsonl");

        Directory.CreateDirectory(_directory);
        File.WriteAllLines(path, videoIds.Select(v => Line(v, confidence)));

        return KeyLedger.Read(path);
    }

    private static string Line(string videoId, string confidence) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp = "2026-08-16T00:00:00+00:00",
                song = $"artist - {videoId}",
                artist = "Artist",
                title = videoId,
                source_url = $"https://music.youtube.com/watch?v={videoId}",
                verdict = new { key = confidence == "WITHHELD" ? null : "C major", confidence },
            }
        );

    private static QueueItem Item(string videoId, bool unavailable = false) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = $"Track {videoId}",
            IsUnavailable = unavailable,
        };

    private static QueueView View(params QueueItem[] outstanding) =>
        new()
        {
            Items = outstanding,
            Outstanding = outstanding,
            FullyProcessed = [],
            Stalled = [],
            Held = [],
        };

    [Fact]
    public void An_empty_queue_yields_nothing()
    {
        Assert.False(new SeparationSelector(Jobs).TryPickNext(View(), NoLedger, out _));
    }

    [Fact]
    public void A_held_track_cannot_be_picked_because_it_is_not_outstanding()
    {
        // The guard is structural, not a check here. QueueService subtracts held tracks from
        // Outstanding, so the selector needs no knowledge of holds and cannot forget to apply
        // them — which matters because Outstanding has several other readers.
        var held = Item("heldheld001");

        var view = new QueueView
        {
            Items = [held],
            Outstanding = [],
            FullyProcessed = [],
            Stalled = [],
            Held = [held],
        };

        Assert.False(new SeparationSelector(Jobs).TryPickNext(view, NoLedger, out _));
    }

    [Fact]
    public void The_first_outstanding_track_is_picked()
    {
        // Outstanding is already oldest-added first, so first is the queue order.
        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            Judged("aaaaaaaaaaa", "bbbbbbbbbbb"),
            out var order
        );

        Assert.True(picked);
        Assert.Equal("aaaaaaaaaaa", order.VideoId);
    }

    [Fact]
    public void A_track_we_already_separated_is_not_separated_again()
    {
        // This is the one that makes draining terminate: a separated track stays "outstanding"
        // until it also has a verdict, so the queue alone would loop on it forever.
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Succeeded);

        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            Judged("aaaaaaaaaaa", "bbbbbbbbbbb"),
            out var order
        );

        Assert.True(picked);
        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_track_already_in_flight_is_skipped(JobStatus status)
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, status);

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            Judged("aaaaaaaaaaa", "bbbbbbbbbbb"),
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void A_failure_is_retried()
    {
        Jobs.Record(
            "aaaaaaaaaaa",
            JobStage.Separation,
            JobStatus.Failed,
            error: "cuda out of memory"
        );

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            Judged("aaaaaaaaaaa"),
            out var order
        );

        Assert.Equal("aaaaaaaaaaa", order.VideoId);
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
            Jobs.Record(
                "aaaaaaaaaaa",
                JobStage.Separation,
                JobStatus.Failed,
                error: "cuda out of memory"
            );

        Assert.False(
            new SeparationSelector(Jobs).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                Judged("aaaaaaaaaaa"),
                out _
            )
        );
    }

    [Fact]
    public void Giving_up_on_one_track_does_not_block_the_rest_of_the_queue()
    {
        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
            Jobs.Record(
                "aaaaaaaaaaa",
                JobStage.Separation,
                JobStatus.Failed,
                error: "cuda out of memory"
            );

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            Judged("aaaaaaaaaaa", "bbbbbbbbbbb"),
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void Cancelling_puts_a_track_back_in_line_rather_than_striking_it()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Cancelled);

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            Judged("aaaaaaaaaaa"),
            out var order
        );

        Assert.Equal("aaaaaaaaaaa", order.VideoId);
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        // It can never succeed, so trying would burn its three strikes for nothing.
        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa", unavailable: true), Item("bbbbbbbbbbb")),
            Judged("aaaaaaaaaaa", "bbbbbbbbbbb"),
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void The_strike_limit_is_configurable_for_a_track_that_deserves_more_patience()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Failed, error: "transient");

        Assert.False(
            new SeparationSelector(Jobs, maxStrikes: 1).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                Judged("aaaaaaaaaaa"),
                out _
            )
        );
        Assert.True(
            new SeparationSelector(Jobs, maxStrikes: 2).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                Judged("aaaaaaaaaaa"),
                out _
            )
        );
    }

    [Fact]
    public void A_track_with_no_verdict_is_not_separated()
    {
        // The Eat Sleep Dance case. An evidence run records nothing in the ledger -- keytag's
        // recorder requires a verdict -- so "detected" and "never touched" are indistinguishable
        // from here, and separation reads BOTH the recipe (has_vocals) and the destination
        // (HasTaggableKey) off a line that does not exist. Separating anyway asked for a vocal
        // stem of a track with no vocals, and stranded the stems in Unresolved Key for good,
        // because nothing moves them once a verdict arrives.
        Assert.False(
            new SeparationSelector(Jobs).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void A_judged_track_is_picked_ahead_of_an_unjudged_one()
    {
        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            Judged("bbbbbbbbbbb"),
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void A_withheld_verdict_still_gates_through()
    {
        // WITHHELD is a line: the track was judged and the answer was "no safe tag". That is
        // precisely what Unresolved Key is for, so it separates -- unlike a track nobody judged.
        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            JudgedAs("WITHHELD", "aaaaaaaaaaa"),
            out var order
        );

        Assert.True(picked);
        Assert.Equal(StemDestination.UnresolvedKey, order.Destination);
    }
}
