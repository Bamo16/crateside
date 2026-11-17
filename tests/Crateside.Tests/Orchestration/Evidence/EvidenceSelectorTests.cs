using System.Diagnostics;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Evidence;

public sealed class EvidenceSelectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private void GiveEvidence(string trackKey)
    {
        GiveAudio(trackKey);
        File.WriteAllText(Set.EvidencePathFor(trackKey), "{}");
    }

    private void GiveAudio(string trackKey) =>
        File.WriteAllText(Path.Combine(Set.Prepare(trackKey), "track.flac"), "audio");

    private static WorkItem Item(string trackKey, bool unavailable = false) =>
        new()
        {
            PlaylistItemId = $"PLI-{trackKey}",
            TrackKey = trackKey,
            Title = $"Track {trackKey}",
            IsUnavailable = unavailable,
        };

    /// <summary>Every item arrives with audio held for it.</summary>
    private WorkListsView View(params WorkItem[] outstanding)
    {
        foreach (var item in outstanding)
            GiveAudio(item.TrackKey);

        return new WorkListsView
        {
            Items = outstanding,
            Outstanding = outstanding,
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };
    }

    [Fact]
    public void An_empty_queue_yields_nothing() =>
        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(View(), out _));

    [Fact]
    public void The_first_outstanding_track_is_picked()
    {
        var picked = new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.True(picked);
        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_track_that_already_has_evidence_is_not_detected_again()
    {
        // Detection records nothing in the ledger, so a detected track is still outstanding.
        GiveEvidence("aaaaaaaaaaa");

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }

    [Fact]
    public void A_track_with_evidence_and_no_kept_lookup_is_picked_for_the_lookup()
    {
        GiveEvidence("aaaaaaaaaaa");

        var picked = new EvidenceSelector(Jobs, Set, pages: Lookups()).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            out var next
        );

        Assert.True(picked);
        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_track_with_evidence_and_a_kept_lookup_is_left_alone()
    {
        GiveEvidence("aaaaaaaaaaa");
        File.WriteAllText(
            Path.Combine(Set.Prepare("aaaaaaaaaaa"), PageLookups.SourcesFileName),
            "[]"
        );

        Assert.False(
            new EvidenceSelector(Jobs, Set, pages: Lookups()).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                out _
            )
        );
    }

    [Fact]
    public void A_track_whose_audio_has_gone_is_still_picked_for_a_missing_lookup()
    {
        GiveEvidence("aaaaaaaaaaa");
        File.Delete(Path.Combine(Set.Prepare("aaaaaaaaaaa"), "track.flac"));

        var view = new WorkListsView
        {
            Items = [Item("aaaaaaaaaaa")],
            Outstanding = [Item("aaaaaaaaaaa")],
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };

        Assert.True(new EvidenceSelector(Jobs, Set, pages: Lookups()).TryPickNext(view, out _));
    }

    [Fact]
    public async Task A_lookup_that_reached_no_site_is_kept_so_the_track_is_not_picked_again()
    {
        GiveEvidence("aaaaaaaaaaa");

        var lookups = Lookups(LookupStatus.Unreachable);
        var selector = new EvidenceSelector(Jobs, Set, pages: lookups);
        var view = View(Item("aaaaaaaaaaa"));
        var order = WorkOrder.Compose(
            new WorkItem { TrackKey = "aaaaaaaaaaa", Title = "Pink Floyd - Money" },
            _store.Read()
        );

        Assert.True(selector.TryPickNext(view, out _));

        var result = await new EvidenceRunner(Set, new NoDetectors(), lookups).RunAsync(
            order,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("not reached", result.Summary);
        Assert.False(selector.TryPickNext(view, out _));
    }

    [Fact]
    public void A_track_that_keeps_failing_its_lookup_is_given_up_on_after_the_strikes()
    {
        GiveEvidence("aaaaaaaaaaa");

        for (var strike = 0; strike < EvidenceSelector.DefaultMaxStrikes; strike++)
            Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, JobStatus.Failed, error: "no lookup");

        Assert.False(
            new EvidenceSelector(Jobs, Set, pages: Lookups()).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                out _
            )
        );
    }

    private readonly LedgerStore _store = TestLedger.Store();

    private PageLookups Lookups(LookupStatus status = LookupStatus.NotFound) =>
        new(new FixedPages(status), Set, _store);

    private sealed class FixedPages(LookupStatus status) : ISourcePages
    {
        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SiteLookup>>([new SiteLookup("Hooktheory", status, [])]);
    }

    private sealed class NoDetectors : IDetectorSet
    {
        public Task<DetectionOutcome> DetectAsync(
            string audioPath,
            RunWindow? window,
            string evidencePath,
            CancellationToken ct = default
        ) => throw new UnreachableException();

        public Task<EvidenceReading> ReadEvidenceAsync(
            string audioPath,
            CancellationToken ct = default
        ) => throw new UnreachableException();

        public Task<string?> KeydetectSha256Async(CancellationToken ct = default) =>
            throw new UnreachableException();

        public Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) => throw new UnreachableException();

        public Task<KeydetectReading> ReadKeydetectAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) => throw new UnreachableException();
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_track_already_in_flight_is_skipped(JobStatus status)
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, status);

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_source_still_being_downloaded_is_left_until_the_download_ends(JobStatus status)
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Download, status);

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }

    [Fact]
    public void A_finished_download_is_detected()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Download, JobStatus.Running);
        Jobs.Record("aaaaaaaaaaa", JobStage.Download, JobStatus.Succeeded);

        new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out var next);

        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_succeeded_run_whose_evidence_has_gone_is_acquired_again()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, JobStatus.Succeeded);

        Assert.True(
            new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out var next)
        );
        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_track_whose_audio_has_not_arrived_is_not_this_stages_yet()
    {
        var waiting = Item("aaaaaaaaaaa");

        var view = View(waiting);

        Directory.Delete(Set.Prepare(waiting.TrackKey), recursive: true);

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(view, out _));
    }

    [Fact]
    public void A_failure_is_retried()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, JobStatus.Failed, error: "download failed");

        Assert.True(new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out _));
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
        for (var i = 0; i < EvidenceSelector.DefaultMaxStrikes; i++)
            Jobs.Record(
                "aaaaaaaaaaa",
                JobStage.Evidence,
                JobStatus.Failed,
                error: "download failed"
            );

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out _));
    }

    [Fact]
    public void Evidence_is_more_patient_than_separation()
    {
        Assert.True(EvidenceSelector.DefaultMaxStrikes > SeparationSelector.DefaultMaxStrikes);
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa", unavailable: true), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }

    [Fact]
    public void Decisions_name_why_each_outstanding_track_is_or_is_not_taken()
    {
        GiveEvidence("aaaaaaaaaaa");
        var view = View(
            Item("aaaaaaaaaaa"),
            Item("bbbbbbbbbbb"),
            Item("ccccccccccc", unavailable: true)
        );
        File.Delete(Directory.EnumerateFiles(Set.Prepare("bbbbbbbbbbb")).Single());
        Jobs.Record("ddddddddddd", JobStage.Evidence, JobStatus.Running);

        var decisions = new EvidenceSelector(Jobs, Set).Decisions(view);

        Assert.Equal(
            [
                new Selection.Skipped(SkipCause.AlreadyHasEvidence),
                new Selection.Skipped(SkipCause.NoAudioYet),
                new Selection.Skipped(SkipCause.Unavailable),
            ],
            decisions.Select(d => d.Selection)
        );
    }
}
