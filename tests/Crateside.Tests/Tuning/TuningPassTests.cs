using AbleKit.Analysis;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Pairing;
using Crateside.Core.Tuning;
using Crateside.Core.Warps;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Tuning;

public sealed class TuningPassTests : IDisposable
{
    private const string TrackKey = "track";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public TuningPassTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Instrumental => Path.Combine(_root, "Song (Instrumental - Full).flac");

    private string Vocal => Path.Combine(_root, "Song (Vocal - Full).flac");

    [Fact]
    public async Task A_steady_detuned_track_gets_the_negated_median_in_every_saved_clip()
    {
        var pass = Pass([16, 18, 17.5, 17, 17.5]);
        Saved(Instrumental, Vocal);

        Assert.True(await pass.SettleAsync(Track(), TestContext.Current.CancellationToken));

        Assert.Equal(-17.5f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        Assert.Equal(-17.5f, AnalysisFile.Read($"{Vocal}.asd").Clip.PitchFine);
    }

    [Fact]
    public async Task A_note_records_the_detune_and_its_readings()
    {
        var notes = new StageNotes(Path.Combine(_root, "notes.jsonl"));
        var pass = Pass([16, 18, 17.5, 17, 17.5], notes: notes);
        Saved(Instrumental);

        await pass.SettleAsync(Track(Instrumental), TestContext.Current.CancellationToken);

        var note = Assert.Single(notes.All());
        Assert.Equal("Detune", note.Stage);
        Assert.Equal("Detuned −17.5 ¢ (readings +16, +18, +17.5, +17, +17.5)", note.Summary);
    }

    [Fact]
    public async Task A_spread_of_nine_is_not_written_and_waits_on_the_review_list()
    {
        var store = new TuningStore();
        var pass = Pass([15, 20, 24, 18, 21], store);
        Saved(Instrumental);

        Assert.False(
            await pass.SettleAsync(Track(Instrumental), TestContext.Current.CancellationToken)
        );

        Assert.Equal(0f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        var (_, entry) = Assert.Single(store.Review());
        Assert.Equal([15, 20, 24, 18, 21], entry.Readings);
    }

    [Fact]
    public async Task A_median_of_9_9_is_left_alone_and_not_reviewed()
    {
        var store = new TuningStore();
        var pass = Pass([9.9, 9.9, 9.9, 9.9, 9.9], store);
        Saved(Instrumental);

        await pass.SettleAsync(Track(Instrumental), TestContext.Current.CancellationToken);

        Assert.Equal(0f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        Assert.Empty(store.Review());
        Assert.Equal(TuningState.InTune, store.Of(TrackKey)!.State);
    }

    [Fact]
    public async Task A_median_of_34_5_goes_to_review_and_accepting_it_writes_the_negation()
    {
        var store = new TuningStore();
        var pass = Pass([34, 34.5, 35, 34.5, 34.5], store);
        Saved(Instrumental);
        var ct = TestContext.Current.CancellationToken;

        await pass.SettleAsync(Track(Instrumental), ct);

        Assert.Equal(0f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        Assert.Single(store.Review());

        Assert.True(pass.Accept(TrackKey));
        await pass.SettleAsync(Track(Instrumental), ct);

        Assert.Equal(-34.5f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        Assert.Empty(store.Review());
    }

    [Fact]
    public async Task A_clip_saved_later_gets_the_detune_without_reading_the_audio_again()
    {
        var detector = new FixedDetectors([16, 18, 17.5, 17, 17.5]);
        var pass = Pass(detector);
        var ct = TestContext.Current.CancellationToken;

        Assert.False(await pass.SettleAsync(Track(Instrumental), ct));

        Saved(Instrumental);

        Assert.True(await pass.SettleAsync(Track(Instrumental), ct));
        Assert.Equal(-17.5f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        Assert.Equal(5, detector.Reads);
    }

    [Fact]
    public async Task Putting_back_restores_the_clips_and_the_pass_does_not_write_again()
    {
        var pass = Pass([16, 18, 17.5, 17, 17.5]);
        Saved(Instrumental, Vocal);
        var ct = TestContext.Current.CancellationToken;
        await pass.SettleAsync(Track(), ct);

        Assert.True(pass.PutBack(TrackKey));
        Assert.False(await pass.SettleAsync(Track(), ct));

        Assert.Equal(0f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
        Assert.Equal(0f, AnalysisFile.Read($"{Vocal}.asd").Clip.PitchFine);
    }

    [Fact]
    public async Task A_detune_the_user_set_in_Live_is_not_written_over()
    {
        var pass = Pass([16, 18, 17.5, 17, 17.5]);
        Saved(Instrumental);
        (
            AnalysisFile.Read($"{Instrumental}.asd") with
            {
                Clip = new Clip { LoopEnd = 5, PitchFine = 12 },
            }
        ).Write($"{Instrumental}.asd");

        await pass.SettleAsync(Track(Instrumental), TestContext.Current.CancellationToken);

        Assert.Equal(12f, AnalysisFile.Read($"{Instrumental}.asd").Clip.PitchFine);
    }

    [Fact]
    public async Task A_track_with_no_audio_to_read_is_not_decided()
    {
        var store = new TuningStore();
        var pass = Pass([16, 18, 17.5, 17, 17.5], store);
        Saved(Instrumental);

        await pass.SettleAsync(
            Track(Instrumental) with
            {
                ReadFrom = null,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Null(store.Of(TrackKey));
    }

    private TuningTrack Track(params string[] samples) =>
        new(
            TrackKey,
            "Artist – Song",
            samples is [] ? [Instrumental, Vocal] : samples,
            Instrumental,
            TimeSpan.FromMinutes(3)
        );

    private static TuningPass Pass(
        double[] readings,
        TuningStore? store = null,
        StageNotes? notes = null
    ) => Pass(new FixedDetectors(readings), store, notes);

    private static TuningPass Pass(
        FixedDetectors detectors,
        TuningStore? store = null,
        StageNotes? notes = null
    ) =>
        new(
            detectors,
            new SidecarWriter(
                new NoDecoder(),
                new NoDetector(),
                new WrittenWarps(),
                new TransientOrigins()
            ),
            store ?? new TuningStore(),
            new FakeTimeProvider(),
            notes
        );

    private static void Saved(params string[] samples)
    {
        foreach (var sample in samples)
        {
            File.WriteAllBytes(sample, [1, 2, 3]);
            new AnalysisFile
            {
                Audio = AudioAnalysis.FromSamples([.. Enumerable.Repeat(0.5f, 88_200)], 2, [], 3),
                Warp = new Warp { Markers = [new(0.5, 1), new(2.5, 5)] },
                Clip = new Clip { LoopStart = 1, LoopEnd = 5 },
                IsDefaultClipSaved = true,
            }.Write($"{sample}.asd");
        }
    }

    private sealed class FixedDetectors(double[] readings) : IDetectorSet
    {
        public int Reads { get; private set; }

        public Task<KeydetectReading> ReadKeydetectAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) =>
            Task.FromResult<KeydetectReading>(
                new KeydetectReading.Read(
                    MusicKey.FromString("C major")!,
                    0.8,
                    [],
                    [],
                    TuningCents: readings[Reads++ % readings.Length]
                )
            );

        public Task<DetectionOutcome> DetectAsync(
            string audioPath,
            RunWindow? window,
            string evidencePath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<EvidenceReading> ReadEvidenceAsync(
            string audioPath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<string?> KeydetectSha256Async(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<KeyLiftReading> ReadKeyLiftsAsync(
            string audioPath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(null);
    }

    private sealed class NoDetector : ITransientDetector
    {
        public string Version => "none";

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [];
    }
}
