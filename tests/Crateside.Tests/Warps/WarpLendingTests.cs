using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Warps;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Warps;

public sealed class WarpLendingTests : IDisposable
{
    private const int Frames = 22_050;
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WarpLending _lending;
    private readonly StageNotes _notes;

    public WarpLendingTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(_paths.LibraryFolder);

        _notes = new StageNotes(Path.Combine(_root, "stage-notes.jsonl"));
        _lending = Lending(new WarpOptions { LendAutomatically = true });
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_recut_stem_in_staging_takes_the_warp_of_its_finished_instrumental()
    {
        var finished = Stem(
            _paths.LibraryFolder,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        Saved(finished);
        var recut = Stem(_paths.StagingFolder, "Song (Vocal - Clean).flac", "Vocal - Clean");

        var loans = await _lending.LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new WarpLoan(recut, new WarpOutcome.Written(finished, null), "abcdefghijk"),
            Assert.Single(loans)
        );
        Assert.True(AnalysisFile.Read($"{recut}.asd").IsDefaultClipSaved);
    }

    [Fact]
    public async Task A_recut_instrumental_keeps_the_transients_Live_found_in_the_one_it_replaces()
    {
        var finished = Stem(
            _paths.LibraryFolder,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        Transient[] live = [new(500, 1), new(9_000, 0.3f)];
        Saved(finished, live);
        var recut = Stem(
            _paths.StagingFolder,
            "Song (Instrumental - Clean).flac",
            "Instrumental - Clean"
        );

        var loans = await _lending.LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new WarpLoan(recut, new WarpOutcome.Written(finished, finished), "abcdefghijk"),
            Assert.Single(loans)
        );
        Assert.Equal(live, AnalysisFile.Read($"{recut}.asd").Audio.Transients);
    }

    [Fact]
    public async Task Saving_one_staged_instrumental_warps_its_staged_siblings()
    {
        var instrumental = Stem(
            _paths.StagingFolder,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        var vocal = Stem(_paths.StagingFolder, "Song (Vocal - Full).flac", "Vocal - Full");
        var drums = Stem(_paths.StagingFolder, "Song (Drums).flac", "Drums - htdemucs_ft");

        var before = await _lending.LendAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(before, loan => loan.Outcome is WarpOutcome.Written);

        Saved(instrumental);

        var written = (await _lending.LendAsync(TestContext.Current.CancellationToken))
            .Where(loan => loan.Outcome is WarpOutcome.Written)
            .Select(loan => loan.Target)
            .Order(StringComparer.Ordinal);

        Assert.Equal([drums, vocal], written);
    }

    [Fact]
    public async Task A_stem_is_not_tried_again_until_its_files_or_its_siblings_change()
    {
        Saved(Stem(_paths.LibraryFolder, "Song (Instrumental - Full).flac", "Instrumental - Full"));
        Stem(_paths.StagingFolder, "Song (Vocal - Full).flac", "Vocal - Full");

        Assert.NotEmpty(await _lending.LendAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _lending.LendAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_lent_warp_is_not_written_again_after_a_restart()
    {
        Saved(Stem(_paths.LibraryFolder, "Song (Instrumental - Full).flac", "Instrumental - Full"));
        var vocal = Stem(_paths.StagingFolder, "Song (Vocal - Full).flac", "Vocal - Full");
        var record = Path.Combine(_root, "written-warps.json");
        var options = new WarpOptions { LendAutomatically = true };

        await Lending(options, written: new WrittenWarps(record))
            .LendAsync(TestContext.Current.CancellationToken);
        var lent = File.GetLastWriteTimeUtc($"{vocal}.asd");

        var again = await Lending(options, written: new WrittenWarps(record))
            .LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new WarpLoan(vocal, new WarpOutcome.AlreadyWarped(), "abcdefghijk"),
            Assert.Single(again)
        );
        Assert.Equal(lent, File.GetLastWriteTimeUtc($"{vocal}.asd"));
    }

    [Fact]
    public async Task Stems_of_an_upload_nothing_is_warped_on_get_starter_warps_at_the_measured_tempo()
    {
        var vocal = Stem(_paths.StagingFolder, "Song (Vocal - Full).flac", "Vocal - Full");
        var instrumental = Stem(
            _paths.StagingFolder,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );

        var loans = await Lending(
                new WarpOptions { StarterWarps = StarterWarps.OnTheGrid },
                bpm: 120,
                steady: true
            )
            .LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [instrumental, vocal],
            loans
                .Where(loan =>
                    loan.Outcome is WarpOutcome.Started(var bpm, _, _) && Math.Abs(bpm - 120) < 0.01
                )
                .Select(loan => loan.Target)
                .Order(StringComparer.Ordinal)
        );
        Assert.All(
            _notes.All(),
            note =>
            {
                Assert.Equal("Warp", note.Stage);
                Assert.Equal("abcdefghijk", note.TrackKey);
                Assert.Matches(
                    @"^(Vocal|Instrumental) - Full: starter warp at 120\.\d\d BPM, marker at \d+\.\d\d s$",
                    note.Summary
                );
            }
        );
        Assert.Equal(2, _notes.All().Count);
    }

    [Fact]
    public async Task Without_a_steady_measured_tempo_a_stem_gets_a_clip_with_Warp_off_and_no_starter()
    {
        var vocal = Stem(_paths.StagingFolder, "Song (Vocal - Full).flac", "Vocal - Full");

        var loans = await Lending(new WarpOptions { StarterWarps = StarterWarps.OnTheGrid })
            .LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new WarpLoan(vocal, new WarpOutcome.Unwarped(null), "abcdefghijk"),
            Assert.Single(loans)
        );
        Assert.Equal(
            "Vocal - Full: Warp off: no tempo reading",
            Assert.Single(_notes.All()).Summary
        );
        Assert.False(AnalysisFile.Read($"{vocal}.asd").Warp.IsWarped);
    }

    [Fact]
    public async Task With_starter_warps_off_a_stem_with_no_tempo_is_left_for_Live()
    {
        var vocal = Stem(_paths.StagingFolder, "Song (Vocal - Full).flac", "Vocal - Full");

        Assert.Empty(
            await Lending(
                    new WarpOptions { LendAutomatically = true, StarterWarps = StarterWarps.Off }
                )
                .LendAsync(TestContext.Current.CancellationToken)
        );
        Assert.False(File.Exists($"{vocal}.asd"));
    }

    [Fact]
    public async Task With_lending_off_a_finished_sibling_still_holds_back_a_starter()
    {
        var finished = Stem(
            _paths.LibraryFolder,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        Saved(finished);
        var recut = Stem(_paths.StagingFolder, "Song (Vocal - Clean).flac", "Vocal - Clean");

        Assert.Empty(
            await Lending(new WarpOptions { StarterWarps = StarterWarps.OnTheGrid }, bpm: 128)
                .LendAsync(TestContext.Current.CancellationToken)
        );
        Assert.False(File.Exists($"{recut}.asd"));
    }

    private WarpLending Lending(
        WarpOptions options,
        double? bpm = null,
        bool steady = false,
        WrittenWarps? written = null
    ) =>
        new(
            _paths,
            new SidecarWriter(
                steady ? SteadyHits.Decoder : new SilenceDecoder(),
                steady ? SteadyHits.Detector() : new NoTransients(),
                written ?? new WrittenWarps(),
                new TransientOrigins()
            ),
            Options.Create(options),
            new Grids(bpm is { } tempo ? new StarterGrid(tempo, null, null) : null),
            notes: _notes
        );

    private static string Stem(string root, string name, string preset)
    {
        var path = Path.Combine(root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: {preset} | source: {Upload}";
        file.Save();

        return path;
    }

    private static void Saved(string path, Transient[]? transients = null) =>
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                new float[Frames * 2],
                2,
                transients ?? [],
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

    private sealed class Grids(StarterGrid? grid) : IStarterGrids
    {
        public StarterGrid? Of(SiblingFile cut) => grid;
    }
}
