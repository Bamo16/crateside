using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Loudness;
using Crateside.Core.Warps;

namespace Crateside.Tests.Loudness;

public sealed class LevellingTests : IDisposable
{
    private const float Plus6Db = 1.9952623f;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WrittenWarps _written = new();
    private readonly LiveAnalyses _waiting = new();
    private readonly LoudnessOptions _options = new();

    public LevellingTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(Path.Combine(_paths.LibraryFolder, "Artist"));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_warp_the_user_saved_in_the_library_is_brought_to_the_target_and_stays_theirs()
    {
        var stem = Sample(
            Path.Combine("Library", "Artist", "Song (Vocal - Full).flac"),
            gain: 0.5f
        );
        var levelling = Levelling(_ => -20);

        Assert.Single(levelling.Unread());
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);
        Assert.Equal([stem], levelling.Level());

        var sidecar = AnalysisFile.Read($"{stem}.asd");
        Assert.Equal(Plus6Db, sidecar.Clip.SampleVolume, 4);
        Assert.Equal(Markers, sidecar.Warp.Markers);
        Assert.False(_written.IsCratesides(stem));
    }

    [Fact]
    public async Task A_sidecar_on_target_is_not_rewritten()
    {
        var stem = Sample(Path.Combine("Staging", "Song (Vocal - Full).flac"), gain: Plus6Db);
        var before = File.GetLastWriteTimeUtc($"{stem}.asd");
        var levelling = Levelling(_ => -20);

        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);

        Assert.Empty(levelling.Level());
        Assert.Equal(before, File.GetLastWriteTimeUtc($"{stem}.asd"));
    }

    [Fact]
    public async Task A_re_save_in_Live_is_levelled_again()
    {
        var stem = Sample(Path.Combine("Staging", "Song (Vocal - Full).flac"), gain: 1);
        var levelling = Levelling(_ => -20);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);
        levelling.Level();

        Saved(stem, gain: 1);
        File.SetLastWriteTimeUtc($"{stem}.asd", DateTime.UtcNow.AddMinutes(1));

        Assert.Equal([stem], levelling.Level());
        Assert.Equal(Plus6Db, AnalysisFile.Read($"{stem}.asd").Clip.SampleVolume, 4);
    }

    [Fact]
    public async Task A_new_target_re_levels_every_sidecar()
    {
        var stem = Sample(Path.Combine("Staging", "Song (Vocal - Full).flac"), gain: 1);
        var levelling = Levelling(_ => -20);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);
        levelling.Level();

        _options.TargetLufs = -18;

        Assert.Equal([stem], levelling.Level());
        Assert.Equal(2, 20 * Math.Log10(AnalysisFile.Read($"{stem}.asd").Clip.SampleVolume), 3);
    }

    [Fact]
    public async Task A_hand_set_gain_on_drums_goes_back_to_0_dB()
    {
        var drums = Sample(Path.Combine("Staging", "Song (Drums).flac"), gain: 0.5f);
        var levelling = Levelling(_ => -17);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);

        Assert.Equal([drums], levelling.Level());
        Assert.Equal(1, AnalysisFile.Read($"{drums}.asd").Clip.SampleVolume);
    }

    [Fact]
    public async Task A_sidecar_with_Live_waits_until_Live_is_done()
    {
        var stem = Sample(Path.Combine("Staging", "Song (Vocal - Full).flac"), gain: 1);
        _waiting.Await(stem, new AwaitedAnalysis("backup.asd", null, false, DateTime.UtcNow));
        var levelling = Levelling(_ => -20);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);

        Assert.Empty(levelling.Level());
        Assert.Equal(1, AnalysisFile.Read($"{stem}.asd").Clip.SampleVolume);
    }

    [Fact]
    public async Task A_sidecar_without_a_saved_clip_is_left_for_Live()
    {
        var stem = Sample(
            Path.Combine("Staging", "Song (Vocal - Full).flac"),
            gain: 1,
            saved: false
        );
        var levelling = Levelling(_ => -20);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);

        Assert.Empty(levelling.Level());
    }

    [Fact]
    public async Task A_starter_levelled_is_still_crateside_s_to_replace()
    {
        var stem = Sample(Path.Combine("Staging", "Song (Vocal - Full).flac"), gain: 1);
        _written.RecordStarter(stem);
        var levelling = Levelling(_ => -20);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);

        Assert.Equal([stem], levelling.Level());
        Assert.True(_written.IsStarter(stem));
    }

    [Fact]
    public async Task Each_sample_is_read_once()
    {
        Sample(Path.Combine("Staging", "Song (Vocal - Full).flac"), gain: 1);
        Sample(Path.Combine("Library", "Artist", "Other (Instrumental - Full).flac"), gain: 1);
        var levelling = Levelling(_ => -20);

        Assert.Equal(2, levelling.Unread().Count);
        await levelling.ReadAsync(levelling.Unread(), TestContext.Current.CancellationToken);
        Assert.Empty(levelling.Unread());
    }

    private static readonly WarpMarker[] Markers = [new(0.1, 0), new(0.4, 1), new(0.41, 1.03125)];

    private Levelling Levelling(Func<string, double> lufsOf)
    {
        var reader = new LoudnessReader(new FakeEbur128(lufsOf));
        var gains = new ClipGains(reader, () => _options);

        return new Levelling(
            _paths,
            reader,
            gains,
            new SidecarWriter(
                new NoDecoder(),
                new OnsetDetector(),
                _written,
                new TransientOrigins()
            ),
            _waiting
        );
    }

    private string Sample(string relative, float gain, bool saved = true)
    {
        var path = Path.Combine(_root, relative);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);
        Saved(path, gain, saved);

        return path;
    }

    private static void Saved(string path, float gain, bool saved = true) =>
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(new float[4410], 2, [], new FileInfo(path).Length),
            Warp = new Warp { Markers = saved ? Markers : [] },
            Clip = new Clip
            {
                LoopEnd = 1,
                LoopOn = false,
                SampleVolume = gain,
            },
            IsDefaultClipSaved = saved,
        }.Write($"{path}.asd");

    private sealed class NoDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(null);
    }
}
