using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Which source the download stage takes next, and in what order.
/// </summary>
public class DownloadSelectorTests : IDisposable
{
    private const string Finished = "RzQRxV2XqFU";
    private const string Fresh = "x3U22KHsrqE";
    private const string Attached = "QC0BBgOdspo";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public DownloadSelectorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            StemsKeyIdentified = Path.Combine(_directory, "identified"),
            StemsUnresolvedKey = Path.Combine(_directory, "unresolved"),
        };

    private DownloadSelector Selector() => new(Jobs, Set, Paths);

    private static readonly AddedSource Source = new() { VideoId = Attached, Label = "A Cappella" };

    /// <summary>
    /// A track the playlist no longer holds, which is what the intake store reads back.
    /// </summary>
    private static QueueItem Absent(string videoId, StemPlan plan) =>
        new()
        {
            VideoId = videoId,
            Title = videoId,
            Plan = plan,
        };

    private static QueueItem Held(string videoId, StemPlan? plan = null) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = videoId,
            Plan = plan ?? StemPlan.Default,
        };

    private static StemPlan WithSource =>
        new() { Presets = ["vocal_full"], AddedSources = [Source] };

    private static StemPlan KeepingTheSource => new() { Presets = [], KeepSource = true };

    /// <summary>A track crateside finished with: in no work list, only still held on record.</summary>
    private static QueueView FinishedWith(StemPlan plan) =>
        new()
        {
            Items = [Absent(Finished, plan)],
            Outstanding = [],
            FullyProcessed = [Absent(Finished, plan)],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };

    private static QueueView View(params QueueItem[] items) =>
        new()
        {
            Items = items,
            Outstanding = items,
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };

    /// <summary>
    /// The fault ADR 0021 was written for: a source attached to a track the playlist had already
    /// let go of was reachable by no work list, and was never fetched.
    /// </summary>
    [Fact]
    public void A_source_attached_to_a_track_the_playlist_has_let_go_is_still_fetched()
    {
        Assert.True(Selector().TryPickNext(View(Absent(Finished, WithSource)), out var next));

        Assert.Equal(Attached, next.VideoId);
        Assert.True(next.IsAdded);
        Assert.Equal("A Cappella", next.Label);
    }

    /// <remarks>
    /// The audio list refills from the playlist while the other two are bounded by something the
    /// user did, so audio going first starves them for as long as anything is arriving.
    /// </remarks>
    [Fact]
    public void An_attached_source_outranks_a_track_still_waiting_for_its_audio()
    {
        var view = View(Absent(Finished, WithSource), Held(Fresh));

        Assert.True(Selector().TryPickNext(view, out var next));
        Assert.Equal(Attached, next.VideoId);
    }

    [Fact]
    public void A_kept_source_outranks_both()
    {
        // A kept source is a copy out of the working set, so it needs the audio already held.
        File.WriteAllText(Path.Combine(Set.Prepare(Finished), "track.flac"), "audio");

        var plan = new StemPlan
        {
            Presets = ["vocal_full"],
            KeepSource = true,
            AddedSources = [Source],
        };
        var view = View(Absent(Finished, plan), Held(Fresh));

        Assert.True(Selector().TryPickNext(view, out var next));
        Assert.Equal(Finished, next.VideoId);
        Assert.True(next.Copy);
    }

    /// <summary>
    /// 1991 - If Only, 2026-09-23: source ticked on a track long finished, whose working set had
    /// been cleared. Nothing fetched it — the copy needs the audio held, and the fetch path only
    /// reads the work lists, which a source-only re-cut is rightly kept out of since 42f3fcf.
    /// </summary>
    [Fact]
    public void A_kept_source_a_re_cut_asked_for_is_fetched_when_its_audio_has_gone()
    {
        // What a source-only re-cut leaves: the separation that already ran, and a handoff owed.
        Jobs.Record(Finished, JobStage.Separation, JobStatus.Succeeded);
        Jobs.Record(Finished, JobStage.Handoff, JobStatus.Cancelled, error: "re-cut requested");

        Assert.True(Selector().TryPickNext(FinishedWith(KeepingTheSource), out var next));

        Assert.Equal(Finished, next.VideoId);
        Assert.False(next.Copy);
        Assert.NotNull(next.Into);
    }

    /// <summary>
    /// Röyksopp - So Easy: keeps its source, separated on 2026-09-02, and nothing of it on disk.
    /// Nobody has asked for it since, so it owes nothing (ADR 0027).
    /// </summary>
    [Fact]
    public void A_finished_track_that_keeps_its_source_is_not_fetched_unasked()
    {
        Jobs.Record(Finished, JobStage.Separation, JobStatus.Succeeded);

        Assert.False(Selector().TryPickNext(FinishedWith(KeepingTheSource), out _));
    }

    [Fact]
    public void Audio_is_still_taken_when_nothing_else_is_owed()
    {
        Assert.True(Selector().TryPickNext(View(Held(Fresh)), out var next));

        Assert.Equal(Fresh, next.VideoId);
        Assert.False(next.IsAdded);
    }

    [Fact]
    public void An_empty_queue_owes_nothing() =>
        Assert.False(Selector().TryPickNext(View(), out _));

    /// <summary>
    /// Audio the janitor swept is not fetched again.
    /// </summary>
    /// <remarks>
    /// The janitor discards a working set once separation has succeeded and staging is clear, so
    /// a finished track looks exactly like an unfetched one. Fetching it made the janitor sweep
    /// it again: 125 downloads of one track in a day.
    /// </remarks>
    [Fact]
    public void A_finished_track_whose_working_set_the_janitor_took_is_not_fetched_again()
    {
        Jobs.Record(Fresh, JobStage.Separation, JobStatus.Succeeded);

        Assert.False(Selector().TryPickNext(View(Held(Fresh)), out _));
    }

    /// <remarks>
    /// A re-cut reopens the separation job, so the latest is no longer a success and the audio
    /// it needs is fetched as usual.
    /// </remarks>
    [Fact]
    public void A_track_put_back_in_line_still_gets_its_audio()
    {
        Jobs.Record(Fresh, JobStage.Separation, JobStatus.Succeeded);
        Jobs.Record(Fresh, JobStage.Separation, JobStatus.Cancelled);

        Assert.True(Selector().TryPickNext(View(Held(Fresh)), out var next));
        Assert.Equal(Fresh, next.VideoId);
    }

    [Fact]
    public void A_track_whose_audio_is_already_held_is_not_fetched_again()
    {
        File.WriteAllText(Path.Combine(Set.Prepare(Fresh), "track.flac"), "audio");

        Assert.False(Selector().TryPickNext(View(Held(Fresh)), out _));
    }
}
