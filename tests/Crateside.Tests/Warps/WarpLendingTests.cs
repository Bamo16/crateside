using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Warps;

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

    public WarpLendingTests()
    {
        _paths = new CratesidePaths
        {
            SampleStaging = Path.Combine(_root, "Sample Staging"),
            MashupSamples = Path.Combine(_root, "Mashup Samples"),
        };
        Directory.CreateDirectory(_paths.SampleStaging);
        Directory.CreateDirectory(_paths.MashupSamples);

        _lending = new WarpLending(
            _paths,
            new WarpBorrower(new SilenceDecoder(), new NoTransients(), new BorrowedWarps())
        );
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_recut_stem_in_staging_takes_the_warp_of_its_finished_instrumental()
    {
        var finished = Stem(
            _paths.MashupSamples,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        Saved(finished);
        var recut = Stem(_paths.SampleStaging, "Song (Vocal - Clean).flac", "Vocal - Clean");

        var loans = await _lending.LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new WarpLoan(recut, new WarpBorrowOutcome.Written(finished, null)),
            Assert.Single(loans)
        );
        Assert.True(AnalysisFile.Read($"{recut}.asd").IsDefaultClipSaved);
    }

    [Fact]
    public async Task A_recut_instrumental_keeps_the_transients_Live_found_in_the_one_it_replaces()
    {
        var finished = Stem(
            _paths.MashupSamples,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        Transient[] live = [new(500, 1), new(9_000, 0.3f)];
        Saved(finished, live);
        var recut = Stem(
            _paths.SampleStaging,
            "Song (Instrumental - Clean).flac",
            "Instrumental - Clean"
        );

        var loans = await _lending.LendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new WarpLoan(recut, new WarpBorrowOutcome.Written(finished, finished)),
            Assert.Single(loans)
        );
        Assert.Equal(live, AnalysisFile.Read($"{recut}.asd").Audio.Transients);
    }

    [Fact]
    public async Task Saving_one_staged_instrumental_warps_its_staged_siblings()
    {
        var instrumental = Stem(
            _paths.SampleStaging,
            "Song (Instrumental - Full).flac",
            "Instrumental - Full"
        );
        var vocal = Stem(_paths.SampleStaging, "Song (Vocal - Full).flac", "Vocal - Full");
        var drums = Stem(_paths.SampleStaging, "Song (Drums).flac", "Drums - htdemucs_ft");

        var before = await _lending.LendAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(before, loan => loan.Outcome is WarpBorrowOutcome.Written);

        Saved(instrumental);

        var written = (await _lending.LendAsync(TestContext.Current.CancellationToken))
            .Where(loan => loan.Outcome is WarpBorrowOutcome.Written)
            .Select(loan => loan.Target)
            .Order(StringComparer.Ordinal);

        Assert.Equal([drums, vocal], written);
    }

    [Fact]
    public async Task A_stem_is_not_tried_again_until_its_files_or_its_siblings_change()
    {
        Stem(_paths.SampleStaging, "Song (Instrumental - Full).flac", "Instrumental - Full");
        Stem(_paths.SampleStaging, "Song (Vocal - Full).flac", "Vocal - Full");

        Assert.NotEmpty(await _lending.LendAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _lending.LendAsync(TestContext.Current.CancellationToken));
    }

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
}
