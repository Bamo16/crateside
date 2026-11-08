using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.Tracks;
using Crateside.Tests.WorkLists;

namespace Crateside.Tests.Tracks;

public sealed class CacheKeeperTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public CacheKeeperTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_scan_of_the_queue_rewrites_the_source_index_with_what_the_working_set_holds()
    {
        var set = new WorkingSet(Path.Combine(_directory, "working"));
        File.WriteAllText(Path.Combine(set.Prepare("dQw4w9WgXcQ"), "Song.flac"), "audio");
        var index = Path.Combine(_directory, "source-index.json");
        var workLists = UnreadableWorkLists.In(
            _directory,
            _ledger,
            new IntakeStore(_ledger),
            out _
        );

        using var keeper = new CacheKeeper(
            workLists,
            Tracks(),
            new SourceIndex(set, index),
            new WarpTempoReader(),
            Paths()
        );

        await workLists.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"videoId\": \"dQw4w9WgXcQ\"", File.ReadAllText(index));
    }

    [Fact]
    public async Task A_rebuild_of_the_rows_saves_the_tempos_it_read()
    {
        var audio = Path.Combine(_directory, "sample.flac");
        File.Copy(Fixture("silence.flac"), audio);
        File.Copy(Fixture("sidecar-clicks.asd"), $"{audio}.asd");
        var cache = Path.Combine(_directory, "tempos.json");
        var tempos = new WarpTempoReader(cache);
        var workLists = UnreadableWorkLists.In(
            _directory,
            _ledger,
            new IntakeStore(_ledger),
            out _
        );
        var set = new WorkingSet(Path.Combine(_directory, "working"));

        using var tracks = Tracks();
        using var keeper = new CacheKeeper(
            workLists,
            tracks,
            new SourceIndex(set, Path.Combine(_directory, "source-index.json")),
            tempos,
            Paths()
        );

        tempos.Read(audio);

        Assert.False(File.Exists(cache));

        await tracks.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(120, new WarpTempoReader(cache).Read(audio)!.Bpm, 4);
    }

    private CratesidePaths Paths() =>
        new() { StemTagCache = Path.Combine(_directory, "stem-tags.json") };

    private static TracksService Tracks() =>
        new(new TracksReads(() => new TracksView { Rows = [] }), new StageHub());

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
