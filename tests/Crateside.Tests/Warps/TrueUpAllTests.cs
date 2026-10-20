using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Warps;
using Crateside.Tests.Tempo;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Warps;

public sealed class TrueUpAllTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WrittenWarps _written = new();
    private readonly FakeTimeProvider _time = new(new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));

    public TrueUpAllTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
            KeptWarpGuides = Path.Combine(_root, "Drums"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(Path.Combine(_paths.LibraryFolder, "Artist"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Every_drifting_warp_in_the_library_is_trued_up_with_its_vocal_and_one_that_holds_is_left()
    {
        var drifting = Song("Drifting", "aaaaaaaaaaa", warpBpm: 119.99);
        var holding = Song("Holding", "bbbbbbbbbbb", warpBpm: 120);
        var before = File.ReadAllBytes($"{holding.Instrumental}.asd");
        var all = new TrueUpAll(_paths, Truer());

        var batch = all.Plan(TestContext.Current.CancellationToken);
        var done = await all.ApplyAsync(batch, TestContext.Current.CancellationToken);

        Assert.Equal((1, 0), (batch.Plans.Count, batch.Refused));
        Assert.Equal("Drifting", Assert.Single(done).Song);
        Assert.All(
            [drifting.Instrumental, drifting.Vocal],
            audio => Assert.Equal(120, AnalysisFile.Read($"{audio}.asd").Warp.TempoAt(10) ?? 0, 6)
        );
        Assert.Equal(before, File.ReadAllBytes($"{holding.Instrumental}.asd"));
    }

    private (string Instrumental, string Vocal) Song(string name, string videoId, double warpBpm)
    {
        var folder = Path.Combine(_paths.LibraryFolder, "Artist");
        var instrumental = Stem(
            Path.Combine(folder, $"{name} (Instrumental - Full).flac"),
            "Instrumental - Full",
            videoId
        );
        var vocal = Stem(
            Path.Combine(folder, $"{name} (Vocal - Full).flac"),
            "Vocal - Full",
            videoId
        );
        var warp = SteadyTrack.Analysis(warpBpm, 120, seconds: 90);
        warp.Write($"{instrumental}.asd");
        warp.Write($"{vocal}.asd");

        return (instrumental, vocal);
    }

    private static string Stem(string path, string preset, string videoId)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.4.0.0 | preset: {preset} | source: https://www.youtube.com/watch?v={videoId}";
        file.Save();

        return path;
    }

    private WarpTrueUp Truer()
    {
        var backups = new SidecarBackups(Path.Combine(_root, "backups"), _time);

        return new(
            new RetypedWarps(),
            _written,
            backups,
            new WarpActions(
                new SidecarWriter(
                    new NoDecoder(),
                    new OnsetDetector(),
                    _written,
                    new TransientOrigins()
                ),
                _written,
                new LiveAnalyses(),
                backups,
                _time
            ),
            _time
        );
    }

    private sealed class NoDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(null);
    }
}
