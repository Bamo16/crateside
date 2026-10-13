using AbleKit.Analysis;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;
using Crateside.Tests.Tempo;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Warps;

public sealed class WarpTrueUpTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";
    private const double Seconds = 90;

    private static readonly WarpMarker[] Users = [new(0.4, 1), new(2.4, 5), new(2.43125, 5.0625)];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly WrittenWarps _written = new();
    private readonly RetypedWarps _retyped = new();
    private readonly FakeTimeProvider _time = new(new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    public WarpTrueUpTests() => Directory.CreateDirectory(_root);

    private string Instrumental => Path.Combine(_root, "Song (Instrumental - Full).flac");

    private string Vocal => Path.Combine(_root, "Song (Vocal - Full).flac");

    private string HandWarpedVocal => Path.Combine(_root, "Song (Vocal - Lead).flac");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Retyping_moves_the_canonical_warp_and_every_copy_crateside_lent_of_it()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120);
        var siblings = WarpSiblings.Scan([_root]);

        var plan = Assert.IsType<TrueUpPlan.Retype>(
            Truer().Plan(siblings, WarpSiblings.Read(Instrumental)!)
        );
        var outcome = await Truer()
            .ApplyAsync(plan, siblings, TestContext.Current.CancellationToken);

        Assert.IsType<TrueUpOutcome.Retyped>(outcome);
        Assert.All(
            [Instrumental, Vocal],
            audio => Assert.Equal(120, AnalysisFile.Read($"{audio}.asd").Warp.TempoAt(10) ?? 0, 6)
        );
    }

    [Fact]
    public async Task A_warp_the_user_made_on_another_stem_is_left_as_it_was()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120);
        var siblings = WarpSiblings.Scan([_root]);
        var plan = Assert.IsType<TrueUpPlan.Retype>(
            Truer().Plan(siblings, WarpSiblings.Read(Instrumental)!)
        );

        await Truer().ApplyAsync(plan, siblings, TestContext.Current.CancellationToken);

        Assert.Equal(Users, AnalysisFile.Read($"{HandWarpedVocal}.asd").Warp.Markers);
    }

    [Fact]
    public async Task The_retyped_warp_stays_the_users_and_is_remembered_with_its_backup()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120);
        var siblings = WarpSiblings.Scan([_root]);
        var before = await File.ReadAllBytesAsync(
            $"{Instrumental}.asd",
            TestContext.Current.CancellationToken
        );
        var plan = Assert.IsType<TrueUpPlan.Retype>(
            Truer().Plan(siblings, WarpSiblings.Read(Instrumental)!)
        );

        await Truer().ApplyAsync(plan, siblings, TestContext.Current.CancellationToken);

        Assert.False(_written.IsCratesides(Instrumental));
        Assert.Equal(120, _retyped.Of(Instrumental)?.Bpm);
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(
                Assert.Single(
                    Directory.GetFiles(Path.Combine(_root, "backups"), "* Song (Instrumental*")
                ),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task A_sidecar_saved_over_since_the_plan_is_not_written()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120);
        var siblings = WarpSiblings.Scan([_root]);
        var plan = Assert.IsType<TrueUpPlan.Retype>(
            Truer().Plan(siblings, WarpSiblings.Read(Instrumental)!)
        );
        File.SetLastWriteTimeUtc($"{Instrumental}.asd", DateTime.UtcNow.AddMinutes(1));

        var outcome = await Truer()
            .ApplyAsync(plan, siblings, TestContext.Current.CancellationToken);

        Assert.IsType<TrueUpOutcome.Changed>(outcome);
        Assert.Equal(119.99, AnalysisFile.Read($"{Instrumental}.asd").Warp.TempoAt(10) ?? 0, 6);
    }

    [Fact]
    public async Task Rephasing_moves_the_grid_onto_the_transients_the_retype_alone_leaves_late()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120, hitsLate: 0.008);
        var siblings = WarpSiblings.Scan([_root]);
        var song = WarpSiblings.Read(Instrumental)!;

        var plain = Assert.IsType<TrueUpPlan.Retype>(Truer().Plan(siblings, song));
        var phased = Assert.IsType<TrueUpPlan.Retype>(Truer().Plan(siblings, song, rephase: true));

        Assert.Equal(0, plain.ShiftSeconds);
        Assert.Equal(0.008, phased.ShiftSeconds, 3);
        Assert.Null(phased.PhasedFrom);
    }

    [Fact]
    public async Task A_kept_drum_stems_transients_set_the_phase_when_there_is_one()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120, hitsLate: 0.008);
        var drums = Path.Combine(_root, "Song (Drums).flac");
        Stem(drums, "Drums - htdemucs_ft");
        SteadyTrack.Analysis(120, 120, seconds: Seconds, hitsLate: 0.012).Write($"{drums}.asd");
        var siblings = WarpSiblings.Scan([_root]);

        var plan = Assert.IsType<TrueUpPlan.Retype>(
            Truer().Plan(siblings, WarpSiblings.Read(Instrumental)!, rephase: true)
        );

        Assert.Equal(0.012, plan.ShiftSeconds, 3);
        Assert.Equal(drums, plan.PhasedFrom);
    }

    [Fact]
    public async Task Applying_a_rephased_plan_leaves_the_transients_centred_on_the_grid()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120, hitsLate: 0.008);
        var siblings = WarpSiblings.Scan([_root]);
        var plan = Assert.IsType<TrueUpPlan.Retype>(
            Truer().Plan(siblings, WarpSiblings.Read(Instrumental)!, rephase: true)
        );

        await Truer().ApplyAsync(plan, siblings, TestContext.Current.CancellationToken);

        var written = AnalysisFile.Read($"{Instrumental}.asd");
        var report = GridCheck.Against(
            GridCheck.Strongest(written.Audio.Transients, SteadyTrack.SampleRate),
            written.Warp
        );
        Assert.InRange(report.MedianOffsetMs, -0.5, 0.5);
        Assert.All(report.OffsetsPer(30), ms => Assert.InRange(ms, -1, 1));
    }

    [Fact]
    public async Task A_warp_that_already_holds_is_left()
    {
        await Setup(warpBpm: 120, trackBpm: 120);

        var plan = Truer().Plan(WarpSiblings.Scan([_root]), WarpSiblings.Read(Instrumental)!);

        Assert.Equal(new TrueUpPlan.Left(TrueUpReason.WithinNoise), plan);
    }

    [Fact]
    public async Task Canonical_stems_with_different_warps_are_left_for_the_user_to_settle()
    {
        await Setup(warpBpm: 119.99, trackBpm: 120);
        var karaoke = Path.Combine(_root, "Song (Karaoke).flac");
        Stem(karaoke, "Karaoke");
        SteadyTrack.Analysis(119.9, 120, seconds: Seconds).Write($"{karaoke}.asd");

        var plan = Truer().Plan(WarpSiblings.Scan([_root]), WarpSiblings.Read(Instrumental)!);

        Assert.IsType<TrueUpPlan.Disagree>(plan);
    }

    /// <summary>An instrumental the user warped, a vocal crateside lent it, and a vocal the user warped by hand.</summary>
    private async Task Setup(double warpBpm, double trackBpm, double hitsLate = 0)
    {
        Stem(Instrumental, "Instrumental - Full");
        Stem(Vocal, "Vocal - Full");
        Stem(HandWarpedVocal, "Vocal - Lead");
        var sibling = SteadyTrack.Analysis(warpBpm, trackBpm, seconds: Seconds, hitsLate: hitsLate);
        sibling.Write($"{Instrumental}.asd");
        (sibling with { Warp = new Warp { Markers = Users } }).Write($"{HandWarpedVocal}.asd");

        var siblings = WarpSiblings.Scan([_root]);
        await Writer()
            .BorrowAsync(
                Vocal,
                siblings.AuthoritiesFor(WarpSiblings.Read(Vocal)!),
                [],
                TestContext.Current.CancellationToken
            );
    }

    private WarpTrueUp Truer() =>
        new(
            _retyped,
            _written,
            new SidecarBackups(Path.Combine(_root, "backups"), _time),
            new WarpActions(
                Writer(),
                _written,
                new LiveAnalyses(),
                new SidecarBackups(Path.Combine(_root, "backups"), _time),
                _time
            ),
            _time
        );

    private SidecarWriter Writer() =>
        new(
            new FixedDecoder(
                new(
                    [.. Enumerable.Repeat(0.5f, (int)(Seconds * SteadyTrack.SampleRate) * 2)],
                    2,
                    SteadyTrack.SampleRate
                )
            ),
            new FixedDetector(),
            _written,
            new TransientOrigins()
        );

    private static void Stem(string path, string? preset)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = string.Join(
            " | ",
            new[] { "stemforge/0.4.0.0", $"preset: {preset}", $"source: {Upload}" }
        );
        file.Save();
    }

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
