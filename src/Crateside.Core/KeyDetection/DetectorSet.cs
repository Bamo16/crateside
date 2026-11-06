using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Pairing;
using Microsoft.ML.OnnxRuntime;
using static Crateside.Core.Ledger.AlgorithmsDto;

namespace Crateside.Core.KeyDetection;

/// <summary>
/// The detectors the bake-off chose (ADR 0055): keydetect for the key, chords and flicker; madmom and
/// Essentia's <c>edma</c> as its backers.
/// </summary>
public sealed class DetectorSet(PcmDecoder decoder, KeydetectFilter keydetect, MadmomKeyCnn madmom)
    : IDetectorSet
{
    /// <summary>Without the escaping meant for HTML, so a title's own script and a pair's <c>+</c> stay readable.</summary>
    private static readonly JsonTypeInfo<MachineDto> Written =
        (JsonTypeInfo<MachineDto>)
            new JsonSerializerOptions(RunEvidenceJsonContext.Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }.GetTypeInfo(typeof(MachineDto));

    /// <summary>keydetect through its pinned build as it stood at start-up, decoding through the configured ffmpeg (ADR 0066).</summary>
    public DetectorSet(IProcessRunner runner, CratesidePaths paths, KnownBuilds builds)
        : this(
            new PcmDecoder(runner, paths.KeydetectFfmpeg),
            new KeydetectFilter(runner, builds.FileNameFor(FfmpegFilter.Keydetect)),
            new MadmomKeyCnn()
        ) { }

    /// <summary>All three through one ffmpeg, which must be a build with <c>keydetect</c>.</summary>
    public static DetectorSet For(IProcessRunner runner, string ffmpeg) =>
        new(
            new PcmDecoder(runner, ffmpeg),
            new KeydetectFilter(runner, ffmpeg),
            new MadmomKeyCnn()
        );

    /// <summary>Detects over the audio, or one window of it, and writes the evidence the judge reads.</summary>
    public async Task<DetectionOutcome> DetectAsync(
        string audioPath,
        RunWindow? window,
        string evidencePath,
        CancellationToken ct = default
    )
    {
        switch (await ReadAsync(audioPath, window, ct))
        {
            case EvidenceReading.Read(var json) reading:
                // A track taken in from a file holds its audio in imports, so no folder is made for it yet.
                Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
                await File.WriteAllTextAsync(evidencePath, json, ct);

                return new DetectionOutcome.Written(evidencePath) { Summary = reading.Summary };
            case EvidenceReading.Failed(var error):
                return new DetectionOutcome.Failed(error);
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>The evidence <see cref="DetectAsync"/> would write over the whole audio, without writing it.</summary>
    public Task<EvidenceReading> ReadEvidenceAsync(
        string audioPath,
        CancellationToken ct = default
    ) => ReadAsync(audioPath, null, ct);

    /// <summary>The hash of the executable keydetect runs on, as the evidence records it.</summary>
    public async Task<string?> KeydetectSha256Async(CancellationToken ct = default) =>
        (await keydetect.IdentifyAsync(ct)).Sha256;

    /// <summary>madmom and edma over the audio, or one window of it, on the CPU alone.</summary>
    public async Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
        string audioPath,
        RunWindow? window,
        CancellationToken ct = default
    ) =>
        await decoder.DecodeAsync(audioPath, window, ct) switch
        {
            DecodeOutcome.Decoded(var samples) => await Task.Run(
                () => (madmom.Read(samples), EdmaKeyEstimator.Read(samples)),
                ct
            ),
            DecodeOutcome.Failed(var error) => (
                new KeyReading.Failed(error),
                new KeyReading.Failed(error)
            ),
            _ => throw new UnreachableException(),
        };

    /// <summary>keydetect over the audio, or one window of it.</summary>
    public Task<KeydetectReading> ReadKeydetectAsync(
        string audioPath,
        RunWindow? window,
        CancellationToken ct = default
    ) => keydetect.ReadAsync(audioPath, window, ct);

    /// <summary>The evidence's own shape holding only the readings given, for adding to a run that lacks them.</summary>
    internal static JsonObject Part(
        KeydetectReading? keydetect,
        KeyReading? madmom,
        KeyReading? edma
    ) =>
        JsonSerializer.SerializeToNode(
            new MachineDto
            {
                Algorithms = new()
                {
                    FfmpegKeydetect = keydetect is null ? null : FfmpegOf(keydetect),
                    Madmom = madmom is null ? null : Simple(madmom),
                    EssentiaEdma = edma is null ? null : Simple(edma),
                },
                Chords = keydetect is KeydetectReading.Read { Chords: var chords } ? chords : null,
                KeyEvents = keydetect is KeydetectReading.Read ? KeyEventsOf(keydetect) : null,
            },
            Written
        ) as JsonObject
        ?? throw new UnreachableException();

    private async Task<EvidenceReading> ReadAsync(
        string audioPath,
        RunWindow? window,
        CancellationToken ct
    )
    {
        if (!File.Exists(audioPath))
            return new EvidenceReading.Failed($"no audio at {audioPath}");

        var filter = keydetect.ReadAsync(audioPath, window, ct);
        var (cnn, edma) = await ReadBackersAsync(audioPath, window, ct);

        var (ffmpeg, sha256) = await keydetect.IdentifyAsync(ct);
        var evidence = Evidence(audioPath, window, await filter, cnn, edma) with
        {
            Versions = new Dictionary<string, string?>
            {
                ["ffmpeg"] = ffmpeg,
                ["ffmpeg_sha256"] = sha256,
                ["onnxruntime"] = typeof(InferenceSession).Assembly.GetName().Version?.ToString(),
                ["madmom_model"] = "key_cnn 2018 (madmom_models), as ONNX",
            },
        };

        return
            evidence.Algorithms
                is { FfmpegKeydetect.Key: null, Madmom.Key: null, EssentiaEdma.Key: null }
            ? new EvidenceReading.Failed(
                $"no detector read a key: {evidence.Algorithms.FfmpegKeydetect.Error}"
            )
            : new EvidenceReading.Read(JsonSerializer.Serialize(evidence, Written))
            {
                Summary = Summarize(evidence.Algorithms),
            };
    }

    /// <summary>What each detector read, for the activity feed (#170).</summary>
    internal static string Summarize(AlgorithmsDto? algorithms)
    {
        static string Said(string name, string? key, string? detail = null) =>
            key is null ? $"{name} no key" : $"{name} {key}{detail}";

        return string.Join(
            ", ",
            [
                Said(
                    "keydetect",
                    algorithms?.FfmpegKeydetect?.Key,
                    algorithms?.FfmpegKeydetect is { R: { } r }
                        ? string.Create(CultureInfo.InvariantCulture, $" (r {r:0.00})")
                        : null
                ),
                Said("madmom", algorithms?.Madmom?.Key),
                Said("edma", algorithms?.EssentiaEdma?.Key),
            ]
        );
    }

    private static AlgorithmsDto.FfmpegDto FfmpegOf(KeydetectReading keydetect) =>
        keydetect switch
        {
            KeydetectReading.Read read => new()
            {
                Key = read.Key.LedgerName,
                R = read.R,
                RFlag = TrustBand(read.R),
                KeyAlt = read.RunnerUp?.LedgerName,
                KeyAltConfidence = read.RunnerUpR,
                KeyReranked = read.Reranked,
            },
            KeydetectReading.Failed(var error) => new() { Error = error },
            _ => throw new UnreachableException(),
        };

    private static IReadOnlyList<MachineDto.KeyEventDto> KeyEventsOf(KeydetectReading keydetect) =>
        keydetect is KeydetectReading.Read { KeyEvents: var events }
            ?
            [
                .. events.Select(change => new MachineDto.KeyEventDto
                {
                    T = change.Seconds,
                    Key = change.Key,
                    R = change.R,
                }),
            ]
            : [];

    internal static MachineDto Evidence(
        string audioPath,
        RunWindow? window,
        KeydetectReading keydetect,
        KeyReading madmom,
        KeyReading edma
    )
    {
        var leader = keydetect is KeydetectReading.Read { Key: var key } ? key : null;
        (string Name, MusicKey? Key)[] backers =
        [
            (MadmomKeyCnn.Name, KeyOf(madmom)),
            (EdmaKeyEstimator.Name, KeyOf(edma)),
        ];

        return new()
        {
            Input = audioPath,
            Segment = window is { Start: var start, End: var end } ? [start, end] : null,
            Algorithms = new()
            {
                FfmpegKeydetect = FfmpegOf(keydetect),
                Madmom = Simple(madmom),
                EssentiaEdma = Simple(edma),
            },
            Backers = leader is null
                ? null
                :
                [
                    .. backers
                        .Where(backer => leader.Equals(backer.Key))
                        .Select(backer => backer.Name),
                ],
            PairwiseAgreement =
            [
                Pair("madmom+ffmpeg", KeyOf(madmom), leader),
                Pair("edma+ffmpeg", KeyOf(edma), leader),
                Pair("madmom+edma", KeyOf(madmom), KeyOf(edma)),
            ],
            Chords = keydetect is KeydetectReading.Read { Chords: var chords } ? chords : [],
            KeyEvents = KeyEventsOf(keydetect),
        };
    }

    /// <summary>
    /// The bands for <c>r</c>, from the 2026-07-19 corpus evaluation of the 8.1.2 build and held for 1.2.1; they
    /// shift with the build, so the evidence records which ffmpeg read it.
    /// </summary>
    private static string TrustBand(double r) =>
        r switch
        {
            < 0.70 => "DISTRUST (r<0.70: unreliable band)",
            < 0.75 => "gray zone (0.70-0.75)",
            _ => "trust band (>0.75; watch for relative-key swap)",
        };

    private static SimpleDto Simple(KeyReading reading) =>
        reading switch
        {
            KeyReading.Read(var key, var confidence) => new()
            {
                Key = key.LedgerName,
                Confidence = Math.Round(confidence, 4),
            },
            KeyReading.Failed(var error) => new() { Error = error },
            _ => throw new UnreachableException(),
        };

    private static MusicKey? KeyOf(KeyReading reading) =>
        reading is KeyReading.Read(var key, _) ? key : null;

    /// <summary>The pair statuses: <c>full</c>, <c>relative</c>, <c>none</c>, or <c>single</c> when one side has no key.</summary>
    private static MachineDto.PairDto Pair(string name, MusicKey? first, MusicKey? second) =>
        (first, second) switch
        {
            ({ } a, { } b) when a.Equals(b) => new()
            {
                Pair = name,
                Status = "full",
                Recommended = a.LedgerName,
            },
            ({ } a, { } b) when a.Relative.Equals(b) => new()
            {
                Pair = name,
                Status = "relative",
                Note = $"relative keys: {a.LedgerName} and {b.LedgerName}",
            },
            ({ } a, { } b) => new()
            {
                Pair = name,
                Status = "none",
                Note = $"{a.LedgerName} against {b.LedgerName}",
            },
            _ => new()
            {
                Pair = name,
                Status = "single",
                Note = "one of the two read no key",
            },
        };
}

public abstract record DetectionOutcome
{
    private DetectionOutcome() { }

    public sealed record Written(string EvidencePath) : DetectionOutcome
    {
        /// <summary>What the detectors read, in a line for the activity feed (#170).</summary>
        public string? Summary { get; init; }
    }

    public sealed record Failed(string Error) : DetectionOutcome;
}

public abstract record EvidenceReading
{
    private EvidenceReading() { }

    public sealed record Read(string Json) : EvidenceReading
    {
        /// <summary>What the detectors read, in a line for the activity feed (#170).</summary>
        public string? Summary { get; init; }
    }

    public sealed record Failed(string Error) : EvidenceReading;
}
