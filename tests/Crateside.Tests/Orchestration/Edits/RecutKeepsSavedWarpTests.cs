using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.Staging;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Edits;

/// <summary>A re-cut that removes the stems a track was warped on must not lose that warp (#157).</summary>
public sealed class RecutKeepsSavedWarpTests : IDisposable
{
    private const int Frames = 22_050;
    private const string Video = "abcdefghijk";

    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"}
        ]
        """
    );

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly List<StagedFile> _staged = [];
    private readonly KeptWarps _kept;

    public RecutKeepsSavedWarpTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
            DataRoot = Path.Combine(_root, "data"),
        };
        _kept = new KeptWarps(_paths, new WrittenWarps());
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(Path.Combine(_root, "working"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_source_cut_after_a_recut_takes_the_warp_saved_on_the_stem_the_recut_removed()
    {
        var instrumental = Stage("Instrumental - Full");
        Stage("Vocal - Full");
        SaveWarp(instrumental);
        var settings = new TrackInstructionStore(
            new LedgerStore(LedgerStore.InMemory, TimeProvider.System)
        );

        var outcome = Recut(settings)
            .Apply(Video, new StemPlan { Presets = [], KeepSource = true }, Catalog);

        Assert.True(outcome.Succeeded);
        Assert.False(File.Exists(instrumental));

        var source = Path.Combine(_root, "working", "Song.flac");
        Tagged(source, preset: null);

        var prepared = await Sidecars()
            .PrepareAsync([source], TestContext.Current.CancellationToken);

        Assert.False(prepared[source].IsUnwarped);
        Assert.NotNull(prepared[source].From);
        Assert.Equal(
            [0.1, 0.4],
            prepared[source].File.Warp.Markers.Select(marker => marker.Seconds)
        );
    }

    private StemRecut Recut(TrackInstructionStore settings)
    {
        var working = new WorkingSet(Path.Combine(_root, "working-set"));
        var folder = working.Prepare(Video);
        File.WriteAllText(Path.Combine(folder, "Song.opus"), "audio");

        return new StemRecut(
            settings,
            working,
            new ListedStagedFiles(() => _staged),
            TimeProvider.System,
            keptWarps: _kept
        );
    }

    private HandoffSidecars Sidecars() =>
        new(
            _paths,
            new SidecarWriter(
                new SilenceDecoder(),
                new NoTransients(),
                new WrittenWarps(),
                new TransientOrigins()
            ),
            new BeatReader(new ProcessRunner()),
            Options.Create(new WarpOptions { LendAutomatically = true }),
            _kept
        );

    private string Stage(string preset)
    {
        var name = $"Song ({preset}).flac";
        var path = Path.Combine(_paths.StagingFolder, name);
        Tagged(path, preset);
        _staged.Add(
            new StagedFile
            {
                Path = path,
                FileName = name,
                Preset = preset,
            }
        );

        return path;
    }

    private static void Tagged(string path, string? preset)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.4.0.0 | {(preset is null ? string.Empty : $"preset: {preset} | ")}source: https://www.youtube.com/watch?v={Video}";
        file.Save();
    }

    private static void SaveWarp(string path) =>
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                new float[Frames * 2],
                2,
                [],
                new FileInfo(path).Length
            ),
            Warp = new Warp { Markers = [new(0.1, 0), new(0.4, 1)] },
            Clip = new Clip
            {
                LoopStart = 0,
                LoopEnd = 1,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        }.Write($"{path}.asd");

    private sealed class SilenceDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(new DecodedAudio(new float[Frames * 2], 2, 44_100));
    }

    private sealed class NoTransients : ITransientDetector
    {
        public string Version => "none";

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [];
    }
}
