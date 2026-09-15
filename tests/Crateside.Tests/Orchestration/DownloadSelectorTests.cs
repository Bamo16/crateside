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

    [Fact]
    public void A_track_whose_audio_is_already_held_is_not_fetched_again()
    {
        File.WriteAllText(Path.Combine(Set.Prepare(Fresh), "track.flac"), "audio");

        Assert.False(Selector().TryPickNext(View(Held(Fresh)), out _));
    }
}
