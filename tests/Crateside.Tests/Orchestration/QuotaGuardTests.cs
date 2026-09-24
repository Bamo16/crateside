using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>Detection and judging cannot be bought twice, whatever puts a track in front of them.</summary>
public class QuotaGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private const string Finished = "RzQRxV2XqFU";
    private const string Detected = "x3U22KHsrqE";
    private const string JudgedId = "QC0BBgOdspo";

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Detection_refuses_a_track_whose_audio_the_janitor_has_discarded()
    {
        var view = View(Item(Finished));

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(view, out _));
    }

    [Fact]
    public void Detection_refuses_a_track_it_has_already_detected()
    {
        GiveAudio(Detected);
        File.WriteAllText(Set.EvidencePathFor(Detected), "{}");

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(View(Item(Detected)), out _));
    }

    [Fact]
    public void Judging_refuses_a_track_the_ledger_has_judged()
    {
        GiveAudio(JudgedId);
        File.WriteAllText(Set.EvidencePathFor(JudgedId), "{}");

        Assert.False(
            new VerdictSelector(Jobs, Set).TryPickNext(
                View(Item(JudgedId)),
                Judged(JudgedId),
                out _
            )
        );
    }

    [Fact]
    public void A_finished_track_back_in_the_outstanding_list_buys_nothing()
    {
        var view = View(Item(Finished));
        var ledger = Judged(Finished);

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(view, out _));
        Assert.False(new VerdictSelector(Jobs, Set).TryPickNext(view, ledger, out _));
    }

    private void GiveAudio(string videoId) =>
        File.WriteAllText(Path.Combine(Set.Prepare(videoId), "track.flac"), "audio");

    private KeyLedger Judged(params string[] videoIds)
    {
        var path = Path.Combine(_directory, "runs.jsonl");

        Directory.CreateDirectory(_directory);
        File.WriteAllLines(
            path,
            videoIds.Select(v =>
                JsonSerializer.Serialize(
                    new
                    {
                        timestamp = "2026-09-15T00:00:00+00:00",
                        song = $"artist - {v}",
                        artist = "Artist",
                        title = v,
                        source_url = $"https://music.youtube.com/watch?v={v}",
                        verdict = new { key = "C major", confidence = "HIGH" },
                    }
                )
            )
        );

        return KeyLedger.Read(path);
    }

    private static QueueItem Item(string videoId) =>
        new() { VideoId = videoId, Title = $"Track {videoId}" };

    private static QueueView View(params QueueItem[] outstanding) =>
        new()
        {
            Items = outstanding,
            Outstanding = outstanding,
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };
}
