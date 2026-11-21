using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration;

public sealed class YouTubeOnlyPathsTests : IDisposable
{
    private const string FileKey = "file~abc123";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_file_track_is_not_filed_under_a_song_by_a_made_up_youtube_url()
    {
        FakeTimeProvider time = new();
        using LedgerStore store = new(LedgerStore.InMemory, time);
        store.Record(
            new RunRecord
            {
                RecordedAt = time.GetUtcNow(),
                Song = "a - b",
                Artist = "a",
                Title = "b",
                SourceUrl = "https://music.youtube.com/watch?v=aaaaaaaaaaa",
                Confidence = KeyConfidence.High,
                Center = new TonalCenter("C", DiatonicMode.Ionian),
            }
        );
        store.Read().TryGetLatestBySong("a - b", out var song);

        var refusal = new LookalikeAnswer(store, new TrackInstructionStore(store)).SameSong(
            FileKey,
            song!
        );

        Assert.NotNull(refusal);
        Assert.Null(store.Read().SongFor(FileKey));
    }

    [Fact]
    public async Task A_file_track_is_never_downloaded_by_the_backfill()
    {
        RecordingRunner processes = new();
        using var store = TestLedger.Store();
        CratesidePaths paths = new() { WorkingFolder = _directory };
        SourceBackfill backfill = new(
            store,
            new WorkingSet(paths),
            new DownloadRunner(processes),
            TimeProvider.System,
            paths
        );

        var results = await backfill.RunAsync([FileKey], ct: TestContext.Current.CancellationToken);

        Assert.Empty(processes.Requests);
        Assert.Empty(results);
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 1,
                    StandardOutput = string.Empty,
                    StandardError = "no",
                }
            );
        }
    }
}
