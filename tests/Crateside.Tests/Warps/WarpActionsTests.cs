using AbleKit.Analysis;
using Crateside.Core.Warps;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Warps;

public sealed class WarpActionsTests : IDisposable
{
    private const int Frames = 44_100;

    private static readonly WarpMarker[] Lent = [new(0.5, 1), new(2.5, 5), new(2.53125, 5.0625)];

    private static readonly WarpMarker[] Users = [new(0.4, 1), new(2.4, 5), new(2.43125, 5.0625)];

    private static readonly Transient[] LiveFound = [new(1_000, 1), new(20_000, 0.4f)];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly WrittenWarps _written = new();
    private readonly LiveAnalyses _waiting = new();
    private readonly FakeTimeProvider _time = new(new(2026, 10, 2, 21, 0, 0, TimeSpan.Zero));

    public WarpActionsTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Target, [1, 2, 3]);
        Analysis(saved: true, Lent).Write($"{Sibling}.asd");
    }

    private string Target => Path.Combine(_root, "Song (Vocal - Full).flac");

    private string Sibling => Path.Combine(_root, "Song (Instrumental - Full).flac");

    private string Backups => Path.Combine(_root, "backups");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Letting_Live_analyse_keeps_a_copy_and_takes_the_sidecar_away()
    {
        var actions = Actions();
        await Writer().BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(
            $"{Target}.asd",
            TestContext.Current.CancellationToken
        );

        var outcome = actions.LetLiveAnalyse(Target);

        Assert.IsType<LetLiveOutcome.Sent>(outcome);
        Assert.False(File.Exists($"{Target}.asd"));
        var awaited = Assert.IsType<AwaitedAnalysis>(_waiting.Of(Target));
        Assert.Equal(Sibling, awaited.From);
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(awaited.Backup, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Once_Live_writes_a_fresh_analysis_the_warp_goes_back_with_Lives_transients()
    {
        var actions = Actions();
        await Writer().BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);
        actions.LetLiveAnalyse(Target);
        Analysis(saved: false, [], LiveFound).Write($"{Target}.asd");

        Assert.Equal([Target], actions.PutBack());

        var restored = AnalysisFile.Read($"{Target}.asd");
        Assert.True(restored.IsDefaultClipSaved);
        Assert.Equal(Lent, restored.Warp.Markers);
        Assert.Equal(LiveFound, restored.Audio.Transients);
        Assert.Equal(Sibling, _written.BorrowedFrom(Target));
        Assert.Null(_waiting.Of(Target));
    }

    [Fact]
    public void Nothing_goes_back_until_Live_has_written_a_fresh_analysis()
    {
        var actions = Actions();
        Analysis(saved: true, Users).Write($"{Target}.asd");
        actions.LetLiveAnalyse(Target);

        Assert.Empty(actions.PutBack());
        Assert.NotNull(_waiting.Of(Target));
    }

    [Fact]
    public void A_warp_saved_in_Live_before_crateside_puts_one_back_is_left_alone()
    {
        var actions = Actions();
        Analysis(saved: true, Lent).Write($"{Target}.asd");
        actions.LetLiveAnalyse(Target);
        Analysis(saved: true, Users).Write($"{Target}.asd");

        Assert.Empty(actions.PutBack());
        Assert.Equal(Users, AnalysisFile.Read($"{Target}.asd").Warp.Markers);
        Assert.Null(_waiting.Of(Target));
    }

    [Fact]
    public async Task Reapplying_replaces_the_users_warp_and_keeps_a_copy_of_it()
    {
        Analysis(saved: true, Users).Write($"{Target}.asd");

        var outcome = await Actions()
            .ReapplyAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.Written>(outcome);
        Assert.Equal(Lent, AnalysisFile.Read($"{Target}.asd").Warp.Markers);
        Assert.Equal(
            Users,
            AnalysisFile.Read(Assert.Single(Directory.GetFiles(Backups))).Warp.Markers
        );
    }

    [Fact]
    public async Task Borrowing_on_its_own_still_never_replaces_the_users_warp()
    {
        Analysis(saved: true, Users).Write($"{Target}.asd");

        var outcome = await Writer()
            .BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.IsType<WarpOutcome.AlreadyWarped>(outcome);
        Assert.Equal(Users, AnalysisFile.Read($"{Target}.asd").Warp.Markers);
    }

    private WarpActions Actions() =>
        new(Writer(), _written, _waiting, new SidecarBackups(Backups, _time), _time);

    private SidecarWriter Writer() =>
        new(
            new FixedDecoder(new([.. Enumerable.Repeat(0.5f, Frames * 2)], 2, 44_100)),
            new FixedDetector(),
            _written,
            new TransientOrigins()
        );

    private static AnalysisFile Analysis(
        bool saved,
        WarpMarker[] markers,
        Transient[]? transients = null
    ) =>
        new()
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, Frames * 2)],
                2,
                transients ?? [],
                3
            ),
            Warp = new Warp { Markers = markers },
            Clip = new Clip
            {
                LoopStart = 1,
                LoopEnd = 5,
                LoopOn = false,
            },
            IsDefaultClipSaved = saved,
        };

    private sealed class FixedDetector : ITransientDetector
    {
        public string Version => "fixed 1";

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [new(1_100, 1)];
    }

    private sealed class FixedDecoder(DecodedAudio audio) : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(audio);
    }
}
