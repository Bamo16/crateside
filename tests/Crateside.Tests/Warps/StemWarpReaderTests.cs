using AbleKit.Analysis;
using Crateside.Core.Warps;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Warps;

public sealed class StemWarpReaderTests : IDisposable
{
    private const int Frames = 44_100 * 4;

    private static readonly WarpMarker[] Lent =
    [
        new(0.5, 1),
        new(1.5, 3),
        new(2.5, 5),
        new(2.515625, 5.03125),
    ];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly WrittenWarps _written = new();
    private readonly LiveAnalyses _waiting = new();

    public StemWarpReaderTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Target, [1, 2, 3]);
        File.WriteAllBytes(Sibling, [1, 2, 3]);
        Analysis(saved: true, Lent).Write($"{Sibling}.asd");
    }

    private string Target => Path.Combine(_root, "Song (Vocal - Full).flac");

    private string Sibling => Path.Combine(_root, "Song (Instrumental - Full).flac");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_stem_Live_has_never_opened_has_no_warp_yet()
    {
        var stem = Reader().Read(Target);

        Assert.IsType<WarpOwner.None>(stem.Owner);
        Assert.Null(stem.Sidecar);
    }

    [Fact]
    public void An_analysis_without_a_saved_clip_is_Lives_and_unwarped()
    {
        Analysis(saved: false, []).Write($"{Target}.asd");

        Assert.IsType<WarpOwner.LiveAnalysis>(Reader().Read(Target).Owner);
    }

    [Fact]
    public async Task A_lent_warp_says_whose_it_is_and_the_lender_says_where_it_went()
    {
        await Writer().BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);

        Assert.Equal(Sibling, Assert.IsType<WarpOwner.Borrowed>(Reader().Read(Target).Owner).From);
        Assert.Equal([Target], Assert.IsType<WarpOwner.Yours>(Reader().Read(Sibling).Owner).LentTo);
    }

    [Fact]
    public async Task A_lent_warp_saved_over_in_Live_is_the_users_and_remembers_it_was_lent()
    {
        await Writer().BorrowAsync(Target, [Sibling], [], TestContext.Current.CancellationToken);
        Analysis(saved: true, [new(0.5, 1), new(1.6, 3), new(2.5, 5), new(2.51, 5.03125)])
            .Write($"{Target}.asd");
        File.SetLastWriteTimeUtc($"{Target}.asd", DateTime.UtcNow.AddMinutes(1));

        var yours = Assert.IsType<WarpOwner.Yours>(Reader().Read(Target).Owner);

        Assert.Equal(Sibling, yours.SavedOver?.From);
    }

    [Fact]
    public void A_stem_left_for_Live_waits_until_Live_writes_one_then_waits_for_crateside()
    {
        Analysis(saved: true, Lent).Write($"{Target}.asd");
        Actions().LetLiveAnalyse(Target);

        Assert.IsType<WarpOwner.WaitingForLive>(Reader().Read(Target).Owner);

        Analysis(saved: false, []).Write($"{Target}.asd");

        Assert.IsType<WarpOwner.PuttingBack>(Reader().Read(Target).Owner);
    }

    [Fact]
    public void The_marker_Live_hides_is_not_drawn()
    {
        var stem = Reader().Read(Sibling);

        Assert.Equal(Lent[..^1], stem.Markers);
        Assert.Equal(4, stem.Seconds);
    }

    [Fact]
    public void A_stem_parts_from_the_shared_warp_at_its_first_marker_of_its_own()
    {
        Analysis(saved: true, [new(0.5, 1), new(1.7, 3), new(2.5, 5), new(2.51, 5.03125)])
            .Write($"{Target}.asd");
        var group = WarpGroup.Of([Reader().Read(Sibling), Reader().Read(Target)]);

        Assert.Equal(Sibling, group.Lender?.Path);
        Assert.Equal(1.5, group.PartsFrom(group.Stems[1]));
        Assert.Null(group.PartsFrom(group.Stems[0]));
    }

    [Fact]
    public void The_overview_is_drawn_from_Lives_bins_for_the_stretch_shown()
    {
        var quiet = Enumerable.Repeat(0.25f, Frames).ToArray();
        quiet.AsSpan(Frames - 2_000).Fill(0.75f);
        (
            Analysis(saved: false, []) with
            {
                Audio = AudioAnalysis.FromSamples(quiet, 2, [], 3),
            }
        ).Write($"{Target}.asd");

        var columns = Reader().Read(Target).Overview(0, 2, columns: 2);

        Assert.Equal(2, columns.Count);
        Assert.Equal(0.25f, columns[0][0].Max, 2);
        Assert.Equal(0.75f, columns[0][1].Max, 2);
    }

    private StemWarpReader Reader() => new(_written, new TransientOrigins(), _waiting);

    private WarpActions Actions()
    {
        var time = new FakeTimeProvider();

        return new(
            Writer(),
            _written,
            _waiting,
            new SidecarBackups(Path.Combine(_root, "backups"), time),
            time
        );
    }

    private SidecarWriter Writer() =>
        new(
            new FixedDecoder(new([.. Enumerable.Repeat(0.5f, Frames * 2)], 2, 44_100)),
            new FixedDetector(),
            _written,
            new TransientOrigins()
        );

    private static AnalysisFile Analysis(bool saved, WarpMarker[] markers) =>
        new()
        {
            Audio = AudioAnalysis.FromSamples([.. Enumerable.Repeat(0.5f, Frames * 2)], 2, [], 3),
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

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [];
    }

    private sealed class FixedDecoder(DecodedAudio audio) : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(audio);
    }
}
