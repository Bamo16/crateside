using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class SidecarWriterTests : IDisposable
{
    private const int Frames = 44_100;

    private static readonly WarpMarker[] Markers = [new(0.5, 1), new(2.5, 5), new(2.53125, 5.0625)];

    private static readonly Transient[] LiveFound = [new(1_000, 1), new(20_000, 0.4f)];

    private static readonly Transient[] Detected = [new(1_100, 1)];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly WrittenWarps _written = new();

    private readonly TransientOrigins _origins = new();

    public SidecarWriterTests() => Directory.CreateDirectory(_root);

    private string Target => Path.Combine(_root, "Song (Vocal - Full).flac");

    private string Sibling => Path.Combine(_root, "Song (Instrumental - Full).flac");

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_stem_takes_its_siblings_warp_with_its_own_waveform()
    {
        Audio(Target);
        Analysis(saved: true, level: 0.25f).Write($"{Sibling}.asd");

        var outcome = await Borrower(level: 0.75f)
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.Written(Sibling, null), outcome);

        var written = AnalysisFile.Read($"{Target}.asd");

        Assert.True(written.IsDefaultClipSaved);
        Assert.Equal(Markers, written.Warp.Markers);
        Assert.Equal(0.75f, written.Audio.Overview.PeakBetween(0, Frames), 2);
        Assert.Equal(Sibling, _written.BorrowedFrom(Target));
    }

    [Fact]
    public async Task A_saved_default_clip_on_the_stem_is_never_replaced()
    {
        Audio(Target);
        Analysis(saved: true).Write($"{Sibling}.asd");
        var own = Analysis(saved: true) with
        {
            Warp = new Warp { Markers = [new(1, 2), new(3, 4)] },
        };
        own.Write($"{Target}.asd");
        var before = await File.ReadAllBytesAsync(
            $"{Target}.asd",
            TestContext.Current.CancellationToken
        );

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.AlreadyWarped>(outcome);
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync($"{Target}.asd", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task An_analysis_Live_wrote_without_a_saved_clip_is_replaced()
    {
        Audio(Target);
        Analysis(saved: true).Write($"{Sibling}.asd");
        Analysis(saved: false).Write($"{Target}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.Written>(outcome);
        Assert.True(AnalysisFile.Read($"{Target}.asd").IsDefaultClipSaved);
    }

    [Fact]
    public async Task A_sibling_without_a_saved_clip_lends_nothing()
    {
        Audio(Target);
        Analysis(saved: false).Write($"{Sibling}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.NoWarpedSibling>(outcome);
        Assert.False(File.Exists($"{Target}.asd"));
    }

    [Fact]
    public async Task A_stem_of_a_different_length_is_refused()
    {
        Audio(Target);
        Analysis(saved: true).Write($"{Sibling}.asd");

        var outcome = await Borrower(frames: Frames - 645)
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.LengthsDiffer(Sibling, Frames, Frames - 645), outcome);
        Assert.False(File.Exists($"{Target}.asd"));
    }

    [Fact]
    public async Task Siblings_holding_different_warps_are_flagged_and_nothing_is_written()
    {
        var other = Path.Combine(_root, "Song (Instrumental - Clean).flac");
        Audio(Target);
        Analysis(saved: true).Write($"{Sibling}.asd");
        (
            Analysis(saved: true) with
            {
                Warp = new Warp { Markers = [new(0.6, 1), new(2.6, 5)] },
            }
        ).Write($"{other}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling, other], [], TestContext.Current.CancellationToken);

        Assert.Equal(
            [Sibling, other],
            Assert.IsType<WarpOutcome.SiblingsDisagree>(outcome).Siblings
        );
        Assert.False(File.Exists($"{Target}.asd"));
    }

    [Fact]
    public async Task Once_Live_saves_over_a_written_warp_it_is_the_users()
    {
        Audio(Target);
        Analysis(saved: true).Write($"{Sibling}.asd");
        await Borrower().BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Analysis(saved: true).Write($"{Target}.asd");
        File.SetLastWriteTimeUtc($"{Target}.asd", DateTime.UtcNow.AddMinutes(1));

        Assert.Null(_written.BorrowedFrom(Target));
    }

    [Fact]
    public async Task A_recut_vocal_takes_the_warp_of_the_vocal_before_it_not_the_instrumentals()
    {
        var earlier = Path.Combine(_root, "Song (Vocal - Lead).flac");
        WarpMarker[] shifted = [new(0.45, 1), new(2.45, 5)];
        Audio(Target);
        Audio(earlier);
        Analysis(saved: true).Write($"{Sibling}.asd");
        (Analysis(saved: true) with { Warp = new Warp { Markers = shifted } }).Write(
            $"{earlier}.asd"
        );

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [earlier], TestContext.Current.CancellationToken);

        Assert.Equal(earlier, Assert.IsType<WarpOutcome.Written>(outcome).From);
        Assert.Equal(shifted, AnalysisFile.Read($"{Target}.asd").Warp.Markers);
    }

    [Fact]
    public async Task Without_a_saved_cut_of_its_kind_a_stem_takes_the_instrumentals_warp()
    {
        var earlier = Path.Combine(_root, "Song (Vocal - Lead).flac");
        Audio(Target);
        Audio(earlier);
        Analysis(saved: true).Write($"{Sibling}.asd");
        Analysis(saved: false).Write($"{earlier}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [earlier], TestContext.Current.CancellationToken);

        Assert.Equal(Sibling, Assert.IsType<WarpOutcome.Written>(outcome).From);
        Assert.Equal(Markers, AnalysisFile.Read($"{Target}.asd").Warp.Markers);
    }

    [Fact]
    public async Task The_transients_Live_found_in_the_stems_own_analysis_are_kept()
    {
        Audio(Target);
        Analysis(saved: true).Write($"{Sibling}.asd");
        Analysis(saved: false, transients: LiveFound).Write($"{Target}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.Written(Sibling, Target), outcome);
        Assert.Equal(LiveFound, AnalysisFile.Read($"{Target}.asd").Audio.Transients);
    }

    [Fact]
    public async Task A_recut_takes_the_transients_Live_found_in_the_cut_of_its_kind()
    {
        var earlier = Path.Combine(_root, "Song (Vocal - Lead).flac");
        Audio(Target);
        Audio(earlier);
        Analysis(saved: true).Write($"{Sibling}.asd");
        Analysis(saved: true, transients: LiveFound).Write($"{earlier}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [earlier], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.Written(earlier, earlier), outcome);
        Assert.Equal(LiveFound, AnalysisFile.Read($"{Target}.asd").Audio.Transients);
    }

    [Fact]
    public async Task An_analysis_of_different_audio_lends_no_transients()
    {
        var earlier = Path.Combine(_root, "Song (Vocal - Lead).flac");
        Audio(Target);
        File.WriteAllBytes(earlier, [1, 2, 3, 4]);
        Analysis(saved: true).Write($"{Sibling}.asd");
        Analysis(saved: false, transients: LiveFound, frames: Frames / 2).Write($"{Target}.asd");
        Analysis(saved: false, transients: LiveFound).Write($"{earlier}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [earlier], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.Written(Sibling, null), outcome);
        Assert.Equal(Detected, AnalysisFile.Read($"{Target}.asd").Audio.Transients);
    }

    [Fact]
    public async Task Transients_crateside_wrote_are_never_taken_for_Lives_even_once_the_warp_is_the_users()
    {
        var earlier = Path.Combine(_root, "Song (Vocal - Lead).flac");
        Audio(Target);
        Audio(earlier);
        Analysis(saved: true).Write($"{Sibling}.asd");
        Analysis(saved: true, transients: LiveFound).Write($"{earlier}.asd");
        _origins.RecordDetected(LiveFound, earlier, "fixed 1");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [earlier], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.Written(earlier, null), outcome);
        Assert.Equal(Detected, AnalysisFile.Read($"{Target}.asd").Audio.Transients);
    }

    [Fact]
    public async Task A_stem_nothing_is_warped_on_gets_one_marker_at_the_start_at_the_judged_tempo()
    {
        Audio(Target);

        var outcome = await Borrower()
            .StartAsync(Target, [], 120, TestContext.Current.CancellationToken);

        Assert.Equal(new WarpOutcome.Started(120, null), outcome);

        var written = AnalysisFile.Read($"{Target}.asd");

        Assert.True(written.IsDefaultClipSaved);
        Assert.Equal([new(0, 0), new(0.015625, 0.03125)], written.Warp.Markers);
        Assert.Equal(new DefaultClip(0, 2), written.DefaultClip);
        Assert.Equal(WarpMode.Complex, written.Warp.Mode);
        Assert.True(_written.IsStarter(Target));
    }

    [Fact]
    public async Task The_transients_a_starter_carries_are_recorded_with_the_detector_that_found_them()
    {
        Audio(Target);

        await Borrower().StartAsync(Target, [], 120, TestContext.Current.CancellationToken);

        Assert.Equal(
            new TransientOrigin("Song (Vocal - Full).flac", "fixed 1", null),
            _origins.Of(AnalysisFile.Read($"{Target}.asd").Audio.Transients, Target)
        );
    }

    [Fact]
    public async Task A_starter_warp_never_lends()
    {
        Audio(Target);
        Audio(Sibling);
        await Borrower().StartAsync(Sibling, [], 120, TestContext.Current.CancellationToken);

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.NoWarpedSibling>(outcome);
    }

    [Fact]
    public async Task A_lent_warp_replaces_a_starter()
    {
        Audio(Target);
        await Borrower().StartAsync(Target, [], 120, TestContext.Current.CancellationToken);
        Analysis(saved: true).Write($"{Sibling}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.Written>(outcome);
        Assert.Equal(Markers, AnalysisFile.Read($"{Target}.asd").Warp.Markers);
        Assert.False(_written.IsStarter(Target));
    }

    [Fact]
    public async Task Once_Live_saves_over_a_starter_it_is_the_users_warp()
    {
        Audio(Target);
        await Borrower().StartAsync(Target, [], 120, TestContext.Current.CancellationToken);

        Analysis(saved: true).Write($"{Target}.asd");
        File.SetLastWriteTimeUtc($"{Target}.asd", DateTime.UtcNow.AddMinutes(1));

        Assert.False(_written.IsStarter(Target));
        Assert.IsType<WarpOutcome.AlreadyWarped>(
            await Borrower().StartAsync(Target, [], 90, TestContext.Current.CancellationToken)
        );
    }

    private SidecarWriter Borrower(float level = 0.5f, int frames = Frames) =>
        new(new FixedDecoder(Samples(level, frames)), new FixedDetector(), _written, _origins);

    private static void Audio(string path) => File.WriteAllBytes(path, [1, 2, 3]);

    private static AnalysisFile Analysis(
        bool saved,
        float level = 0.5f,
        Transient[]? transients = null,
        int frames = Frames
    ) =>
        new()
        {
            Audio = AudioAnalysis.FromSamples(
                Samples(level, frames).Samples,
                2,
                transients ?? [],
                3
            ),
            Warp = new Warp { Markers = saved ? Markers : [] },
            Clip = new Clip
            {
                LoopStart = 1,
                LoopEnd = 5,
                LoopOn = false,
            },
            IsDefaultClipSaved = saved,
        };

    private static DecodedAudio Samples(float level, int frames) =>
        new([.. Enumerable.Repeat(level, frames * 2)], 2, 44_100);

    private sealed class FixedDetector : ITransientDetector
    {
        public string Version => "fixed 1";

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) => Detected;
    }

    private sealed class FixedDecoder(DecodedAudio audio) : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(audio);
    }
}
