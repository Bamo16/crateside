using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class RejudgeTests : IDisposable
{
    private const string Owing = "rejudge0001";
    private const string Intake = "intake00001";
    private const string Backfill = "backfill001";
    private const string JudgedBefore = "2026-10-05T00:00:00+00:00";
    private const string JudgedAfter = "2026-10-23T08:00:00+00:00";

    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse(
        "2026-10-22T19:45:44+00:00"
    );

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));
    private string QueuePath => Path.Combine(_directory, "rejudge.json");

    public RejudgeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private RejudgeQueue Queue(params string[] seeded) =>
        new(
            QueuePath,
            [
                .. seeded.Select(videoId => new RejudgeEntry
                {
                    Song = $"artist - {videoId}",
                    Before = Cutoff,
                    Reason = "test",
                }),
            ]
        );

    private static object Run(string videoId, string at) =>
        new
        {
            timestamp = at,
            song = $"artist - {videoId}",
            artist = "Artist",
            title = videoId,
            source_url = $"https://music.youtube.com/watch?v={videoId}",
            verdict = new { key = "C major", confidence = "HIGH" },
        };

    private KeyLedger Ledger(params object[] lines)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():n}.jsonl");

        File.WriteAllLines(path, lines.Select(line => JsonSerializer.Serialize(line)));

        return TestLedger.From(path);
    }

    private void GiveEvidence(string videoId)
    {
        Set.Prepare(videoId);
        File.WriteAllText(Set.EvidencePathFor(videoId), "{}");
    }

    private static WorkItem Item(string videoId, bool backfill = false) =>
        new()
        {
            VideoId = videoId,
            Title = $"Track {videoId}",
            IsBackfill = backfill,
        };

    private static WorkListsView View(params WorkItem[] outstanding) =>
        new()
        {
            Items = outstanding,
            Outstanding = outstanding,
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };

    [Fact]
    public void A_song_judged_before_its_cutoff_is_owed_a_re_judge()
    {
        var owed = Queue(Owing).Pending(Ledger(Run(Owing, JudgedBefore)));

        Assert.Equal(Owing, Assert.Single(owed).SourceVideoId);
    }

    [Fact]
    public void A_song_leaves_the_queue_once_a_run_from_its_cutoff_on_is_recorded()
    {
        var ledger = Ledger(Run(Owing, JudgedBefore), Run(Owing, JudgedAfter));

        Assert.Empty(Queue(Owing).Pending(ledger));
    }

    [Fact]
    public void A_restatement_to_another_upload_is_not_a_new_judgment()
    {
        var ledger = Ledger(
            Run(Owing, JudgedBefore),
            new
            {
                timestamp = JudgedAfter,
                restates = JudgedBefore,
                source_url = "https://music.youtube.com/watch?v=another0001",
            }
        );

        Assert.Single(Queue(Owing).Pending(ledger));
    }

    [Fact]
    public void Seeding_twice_queues_each_song_once()
    {
        _ = Queue(Owing).Read();

        var again = Queue(Owing).Read();

        Assert.Equal($"artist - {Owing}", Assert.Single(again).Song);
    }

    [Fact]
    public void A_re_judged_song_is_not_owed_again_when_seeding_restores_its_entry()
    {
        var ledger = Ledger(Run(Owing, JudgedBefore), Run(Owing, JudgedAfter));

        _ = Queue(Owing).Read();
        File.Delete(QueuePath);

        Assert.Empty(Queue(Owing).Pending(ledger));
    }

    [Fact]
    public void A_song_added_later_stays_queued_across_restarts()
    {
        Queue().Add($"artist - {Owing}", Cutoff, "added by hand");

        Assert.Single(Queue().Pending(Ledger(Run(Owing, JudgedBefore))));
    }

    [Fact]
    public void Adding_a_queued_song_again_keeps_the_later_cutoff()
    {
        var queue = Queue();

        queue.Add($"artist - {Owing}", Cutoff, "first");
        queue.Add($"artist - {Owing}", Cutoff.AddDays(1), "second");
        queue.Add($"artist - {Owing}", Cutoff.AddDays(-1), "third");

        Assert.Equal(Cutoff.AddDays(1), Assert.Single(queue.Read()).Before);
    }

    [Fact]
    public void The_shipped_seed_names_five_songs_of_149_without_queen()
    {
        var songs = new RejudgeQueue(Options.Create(new CratesidePaths { RejudgeFile = QueuePath }))
            .Read()
            .Select(entry => entry.Song)
            .ToList();

        Assert.Equal(5, songs.Count);
        Assert.Contains("seal - kiss from a rose", songs);
        Assert.DoesNotContain("queen - bohemian rhapsody", songs);
    }

    [Fact]
    public void New_intake_is_judged_before_a_re_judge_and_a_re_judge_before_the_backfill()
    {
        GiveEvidence(Intake);
        GiveEvidence(Backfill);
        GiveEvidence(Owing);
        var owed = Queue(Owing).Pending(Ledger(Run(Owing, JudgedBefore)));
        var selector = new VerdictSelector(Jobs, Set);

        Assert.True(
            selector.TryPickIdle(
                View(Item(Backfill, backfill: true), Item(Intake)),
                KeyLedger.Empty,
                owed,
                includeIntake: true,
                out var first,
                out var firstIsRejudge
            )
        );
        Assert.Equal((Intake, false), (first.VideoId, firstIsRejudge));

        Assert.True(
            selector.TryPickIdle(
                View(Item(Backfill, backfill: true)),
                KeyLedger.Empty,
                owed,
                includeIntake: true,
                out var second,
                out var secondIsRejudge
            )
        );
        Assert.Equal((Owing, true), (second.VideoId, secondIsRejudge));

        Assert.True(
            selector.TryPickIdle(
                View(Item(Backfill, backfill: true)),
                KeyLedger.Empty,
                [],
                includeIntake: true,
                out var third,
                out var thirdIsRejudge
            )
        );
        Assert.Equal((Backfill, false), (third.VideoId, thirdIsRejudge));
    }

    [Fact]
    public void Paused_judging_leaves_intake_waiting_while_a_re_judge_goes()
    {
        GiveEvidence(Intake);
        var owed = Queue(Owing).Pending(Ledger(Run(Owing, JudgedBefore)));

        new VerdictSelector(Jobs, Set).TryPickIdle(
            View(Item(Intake)),
            KeyLedger.Empty,
            owed,
            includeIntake: false,
            out var next,
            out _
        );

        Assert.Equal(Owing, next.VideoId);
    }

    [Fact]
    public void Picking_intake_or_backfill_never_returns_a_judged_song_owed_a_re_judge()
    {
        GiveEvidence(Owing);
        var view = View(Item(Owing), Item(Owing, backfill: true));
        var selector = new VerdictSelector(Jobs, Set);
        var ledger = Ledger(Run(Owing, JudgedBefore));

        Assert.False(selector.TryPickNext(view, ledger, out _));
        Assert.False(selector.TryPickBackfill(view, ledger, out _));
    }

    [Fact]
    public void A_re_judge_that_has_used_up_its_strikes_is_left_alone()
    {
        var owed = Queue(Owing).Pending(Ledger(Run(Owing, JudgedBefore)));

        for (var i = 0; i < VerdictSelector.DefaultMaxStrikes; i++)
            Jobs.Record(Owing, JobStage.Verdict, JobStatus.Failed, error: "no verdict");

        Assert.False(
            new VerdictSelector(Jobs, Set).TryPickIdle(
                View(),
                KeyLedger.Empty,
                owed,
                includeIntake: true,
                out _,
                out _
            )
        );
    }
}
