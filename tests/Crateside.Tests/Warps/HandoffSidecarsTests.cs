using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class HandoffSidecarsTests : IDisposable
{
    private const int Frames = 44_100;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WrittenWarps _written = new();
    private readonly TransientOrigins _origins = new();
    private readonly SidecarWriter _writer;

    public HandoffSidecarsTests()
    {
        _paths = new CratesidePaths
        {
            SampleStaging = Path.Combine(_root, "Sample Staging"),
            MashupSamples = Path.Combine(_root, "Mashup Samples"),
        };
        Directory.CreateDirectory(Path.Combine(_root, "working"));
        _writer = new SidecarWriter(new SilenceDecoder(), new OneTransient(), _written, _origins);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_stem_lands_in_staging_with_its_sidecar_recorded_where_it_landed()
    {
        var working = Path.Combine(_root, "working", "Song (Vocal - Full).flac");
        await File.WriteAllBytesAsync(working, [1, 2, 3], TestContext.Current.CancellationToken);
        var plan = await _writer.PrepareStarterAsync(
            working,
            [],
            128,
            TestContext.Current.CancellationToken
        );
        var prepared = new Dictionary<string, PreparedSidecar>
        {
            [working] = Assert.IsType<SidecarPlan.Ready>(plan).Sidecar,
        };
        var sidecars = new HandoffSidecars(
            _paths,
            _writer,
            new BeatReader(),
            new WarpOptions { WriteStarterWarps = true }
        );

        new HandoffRunner(_paths).Run(
            [working],
            moved: (from, to) => sidecars.Moved(prepared, from, to)
        );

        var staged = Path.Combine(_paths.SampleStaging, "Song (Vocal - Full).flac");
        Assert.True(AnalysisFile.Read($"{staged}.asd").IsDefaultClipSaved);
        Assert.True(_written.IsStarter(staged));
        Assert.False(File.Exists($"{working}.asd"));
        Assert.Equal(
            "onsets test",
            _origins.Of(AnalysisFile.Read($"{staged}.asd").Audio.Transients, staged)?.Detector
        );
    }

    private sealed class SilenceDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(new DecodedAudio(new float[Frames * 2], 2, 44_100));
    }

    private sealed class OneTransient : ITransientDetector
    {
        public string Version => "onsets test";

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [new(1_000, 1)];
    }
}
