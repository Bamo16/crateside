using System.Runtime.InteropServices;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Evidence;

/// <summary>Running the detectors over audio crateside already holds (ADR 0019).</summary>
public sealed class EvidenceRunnerTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private const string KeydetectSummary = """
        [Parsed_keydetect_0 @ 0000025c7e797d00] t=3.20 key=F#m (r=0.66)
        [Parsed_keydetect_0 @ 0000025c7e797d00] t=6.80 key=A (r=0.81)
        lavfi.keydetect.key=A
        lavfi.keydetect.key_confidence=0.812
        lavfi.keydetect.key_alt=F#m
        lavfi.keydetect.key_alt_confidence=0.703
        lavfi.keydetect.key_reranked=1
        lavfi.keydetect.chords=A-A-E-F#m-D-A
        """;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string Working => Path.Combine(_directory, "working");
    private WorkingSet Set => field ??= new WorkingSet(Working);

    public EvidenceRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static WorkOrder Order() =>
        WorkOrder.Compose(
            new WorkItem
            {
                PlaylistItemId = "PLI1",
                VideoId = VideoId,
                Title = "Yes - Owner of a Lonely Heart",
            },
            TestLedger.From("does-not-exist")
        );

    private string Held()
    {
        var audio = Path.Combine(Set.Prepare(VideoId), "Yes - Owner of a Lonely Heart.flac");
        File.WriteAllText(audio, "audio");

        return audio;
    }

    private EvidenceRunner Runner(FakeFfmpeg ffmpeg) => new(Set, DetectorSet.For(ffmpeg, "ffmpeg"));

    private static MachineDto Read(string path) =>
        System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllText(path),
            RunEvidenceJsonContext.Default.MachineDto
        )!;

    [Fact]
    public async Task A_finished_run_writes_all_three_detectors_and_says_where()
    {
        Held();

        var result = await Runner(new FakeFfmpeg(KeydetectSummary))
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Set.EvidencePathFor(VideoId), result.EvidencePath);

        var evidence = Read(result.EvidencePath!);
        Assert.Equal(
            ["ffmpeg keydetect", "madmom", "Essentia edma"],
            AlgorithmResult.FromDto(evidence.Algorithms).Select(algorithm => algorithm.Name)
        );
        Assert.Equal("A major", evidence.Algorithms!.FfmpegKeydetect!.Key);
        Assert.Equal(0.812, evidence.Algorithms.FfmpegKeydetect.R);
        Assert.Equal("F# minor", evidence.Algorithms.FfmpegKeydetect.KeyAlt);
        Assert.Equal(0.703, evidence.Algorithms.FfmpegKeydetect.KeyAltConfidence);
        Assert.True(evidence.Algorithms.FfmpegKeydetect.KeyReranked);
        Assert.Equal("A major", evidence.Algorithms.EssentiaEdma!.Key);
        Assert.NotNull(evidence.Algorithms.Madmom!.Key);
        Assert.Equal(["A", "E", "F#m", "D", "A"], evidence.Chords);
        Assert.Equal(["F#m", "A"], evidence.KeyEvents!.Select(change => change.Key));
        Assert.Contains(EdmaKeyEstimator.Name, evidence.Backers!);
        Assert.Null(evidence.Segment);
    }

    [Fact]
    public async Task Evidence_already_on_disk_is_not_produced_again()
    {
        Held();
        File.WriteAllText(Set.EvidencePathFor(VideoId), "{}");
        var ffmpeg = new FakeFfmpeg(KeydetectSummary);

        var result = await Runner(ffmpeg).RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(ffmpeg.Requests);
    }

    [Fact]
    public async Task A_track_with_no_audio_held_for_it_is_not_this_stages_to_run()
    {
        var result = await Runner(new FakeFfmpeg(KeydetectSummary))
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("working set", result.Error);
    }

    [Fact]
    public async Task Keydetect_failing_leaves_the_backers_reading_and_no_one_backed()
    {
        Held();

        var result = await Runner(
                new FakeFfmpeg(keydetectStderr: "Invalid data found", keydetectExit: 1)
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        var evidence = Read(result.EvidencePath!);
        Assert.Contains("Invalid data found", evidence.Algorithms!.FfmpegKeydetect!.Error);
        Assert.Equal("A major", evidence.Algorithms.EssentiaEdma!.Key);
        Assert.Null(evidence.Backers);
    }

    [Fact]
    public async Task Audio_nothing_can_read_produces_no_evidence_and_says_why()
    {
        Held();

        var result = await Runner(
                new FakeFfmpeg(
                    keydetectStderr: "moov atom not found",
                    keydetectExit: 1,
                    decodes: false
                )
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("moov atom not found", result.Error);
        Assert.False(Set.HasEvidence(VideoId));
    }

    [Fact]
    public async Task A_window_is_decoded_alone_and_recorded_with_its_evidence()
    {
        var audio = Held();
        var ffmpeg = new FakeFfmpeg(KeydetectSummary);
        var path = Path.Combine(_directory, "window.json");

        var result = await Runner(ffmpeg)
            .DetectWindowAsync(
                audio,
                new RunWindow(33.5, 48, "Chorus", WindowAsker.User),
                path,
                TestContext.Current.CancellationToken
            );

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([33.5, 48], Read(path).Segment);
        Assert.All(
            ffmpeg.Requests.Where(request => request.Arguments.Contains("-i")),
            request =>
                Assert.Equal(
                    ["-ss", "33.5", "-t", "14.5", "-i"],
                    request.Arguments.SkipWhile(argument => argument != "-ss").Take(5)
                )
        );
    }

    /// <summary>
    /// ffmpeg as the detectors see it: decoding writes ten seconds of an A major chord, keydetect
    /// prints what it is given.
    /// </summary>
    private sealed class FakeFfmpeg(
        string keydetectStderr,
        int keydetectExit = 0,
        bool decodes = true
    ) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public async Task<ProcessOutcome> RunAsync(
            ProcessRequest request,
            CancellationToken ct = default
        )
        {
            Requests.Add(request);

            if (request.Arguments.Contains("-version"))
                return Outcome(0, stdout: "ffmpeg version 9.0-test");

            if (request.Arguments.Contains("keydetect"))
                return Outcome(keydetectExit, stderr: keydetectStderr);

            if (!decodes)
                return Outcome(1, stderr: "moov atom not found");

            await File.WriteAllBytesAsync(request.Arguments[^1], ChordPcm(), ct);

            return Outcome(0);
        }

        private static ProcessOutcome Outcome(int exit, string stdout = "", string stderr = "") =>
            new()
            {
                ExitCode = exit,
                StandardOutput = stdout,
                StandardError = stderr,
            };

        private static byte[] ChordPcm()
        {
            double[] frequencies = [110, 220, 277.18, 329.63, 440, 554.37, 659.26];
            var samples = new float[PcmDecoder.SampleRate * 10];

            for (var i = 0; i < samples.Length; i++)
            {
                var t = (double)i / PcmDecoder.SampleRate;
                samples[i] = (float)(
                    frequencies.Sum(frequency => Math.Sin(2 * Math.PI * frequency * t)) * 0.1
                );
            }

            return MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        }
    }
}
