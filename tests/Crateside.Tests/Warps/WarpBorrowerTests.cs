using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class WarpBorrowerTests : IDisposable
{
    private const int Frames = 44_100;

    private static readonly WarpMarker[] Markers = [new(0.5, 1), new(2.5, 5), new(2.53125, 5.0625)];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly BorrowedWarps _borrowed = new();

    public WarpBorrowerTests() => Directory.CreateDirectory(_root);

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
        Analysis(Sibling, saved: true, level: 0.25f).Write($"{Sibling}.asd");

        var outcome = await Borrower(level: 0.75f)
            .BorrowAsync(Target, [Sibling], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpBorrowOutcome.Written(Sibling), outcome);

        var written = AnalysisFile.Read($"{Target}.asd");

        Assert.True(written.IsDefaultClipSaved);
        Assert.Equal(Markers, written.Warp.Markers);
        Assert.Equal(0.75f, written.Audio.Overview.PeakBetween(0, Frames), 2);
        Assert.Equal(Sibling, _borrowed.BorrowedFrom(Target));
    }

    [Fact]
    public async Task A_saved_default_clip_on_the_stem_is_never_replaced()
    {
        Audio(Target);
        Analysis(Sibling, saved: true).Write($"{Sibling}.asd");
        var own = Analysis(Target, saved: true) with
        {
            Warp = new Warp { Markers = [new(1, 2), new(3, 4)] },
        };
        own.Write($"{Target}.asd");
        var before = await File.ReadAllBytesAsync(
            $"{Target}.asd",
            TestContext.Current.CancellationToken
        );

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], TestContext.Current.CancellationToken);

        Assert.IsType<WarpBorrowOutcome.AlreadyWarped>(outcome);
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync($"{Target}.asd", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task An_analysis_Live_wrote_without_a_saved_clip_is_replaced()
    {
        Audio(Target);
        Analysis(Sibling, saved: true).Write($"{Sibling}.asd");
        Analysis(Target, saved: false).Write($"{Target}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], TestContext.Current.CancellationToken);

        Assert.IsType<WarpBorrowOutcome.Written>(outcome);
        Assert.True(AnalysisFile.Read($"{Target}.asd").IsDefaultClipSaved);
    }

    [Fact]
    public async Task A_sibling_without_a_saved_clip_lends_nothing()
    {
        Audio(Target);
        Analysis(Sibling, saved: false).Write($"{Sibling}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling], TestContext.Current.CancellationToken);

        Assert.IsType<WarpBorrowOutcome.NoWarpedSibling>(outcome);
        Assert.False(File.Exists($"{Target}.asd"));
    }

    [Fact]
    public async Task A_stem_of_a_different_length_is_refused()
    {
        Audio(Target);
        Analysis(Sibling, saved: true).Write($"{Sibling}.asd");

        var outcome = await Borrower(frames: Frames - 645)
            .BorrowAsync(Target, [Sibling], TestContext.Current.CancellationToken);

        Assert.Equal(new WarpBorrowOutcome.LengthsDiffer(Sibling, Frames, Frames - 645), outcome);
        Assert.False(File.Exists($"{Target}.asd"));
    }

    [Fact]
    public async Task Siblings_holding_different_warps_are_flagged_and_nothing_is_written()
    {
        var other = Path.Combine(_root, "Song (Instrumental - Clean).flac");
        Audio(Target);
        Analysis(Sibling, saved: true).Write($"{Sibling}.asd");
        (
            Analysis(other, saved: true) with
            {
                Warp = new Warp { Markers = [new(0.6, 1), new(2.6, 5)] },
            }
        ).Write($"{other}.asd");

        var outcome = await Borrower()
            .BorrowAsync(Target, [Sibling, other], TestContext.Current.CancellationToken);

        Assert.Equal(
            [Sibling, other],
            Assert.IsType<WarpBorrowOutcome.SiblingsDisagree>(outcome).Siblings
        );
        Assert.False(File.Exists($"{Target}.asd"));
    }

    [Fact]
    public async Task Once_Live_saves_over_a_borrowed_warp_it_is_the_users()
    {
        Audio(Target);
        Analysis(Sibling, saved: true).Write($"{Sibling}.asd");
        await Borrower().BorrowAsync(Target, [Sibling], TestContext.Current.CancellationToken);

        Analysis(Target, saved: true).Write($"{Target}.asd");
        File.SetLastWriteTimeUtc($"{Target}.asd", DateTime.UtcNow.AddMinutes(1));

        Assert.Null(_borrowed.BorrowedFrom(Target));
    }

    private WarpBorrower Borrower(float level = 0.5f, int frames = Frames) =>
        new(new FixedDecoder(Samples(level, frames)), new NoTransients(), _borrowed);

    private static void Audio(string path) => File.WriteAllBytes(path, [1, 2, 3]);

    private static AnalysisFile Analysis(string path, bool saved, float level = 0.5f) =>
        new()
        {
            Audio = AudioAnalysis.FromSamples(Samples(level, Frames).Samples, 2, [], 3),
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

    private sealed class FixedDecoder(DecodedAudio audio) : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(audio);
    }
}
