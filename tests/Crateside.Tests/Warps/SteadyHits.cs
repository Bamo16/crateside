using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

/// <summary>70 s of audio with a hit every half second, 120 BPM, which a starter's grid holds on unless the hits jump.</summary>
internal static class SteadyHits
{
    public const int Seconds = 70;
    private const int SampleRate = 44_100;

    public static IAudioDecoder Decoder { get; } = new LongDecoder();

    /// <param name="jumpAt">Where every later hit moves by <paramref name="jumpSeconds"/>, as a track that steps does.</param>
    public static ITransientDetector Detector(
        double jumpAt = double.PositiveInfinity,
        double jumpSeconds = 0
    ) => new Hits(jumpAt, jumpSeconds);

    private sealed class LongDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(
                new DecodedAudio(new float[Seconds * SampleRate * 2], 2, SampleRate)
            );
    }

    private sealed class Hits(double jumpAt, double jumpSeconds) : ITransientDetector
    {
        public string Version => "steady 1";

        public IReadOnlyList<Transient> Detect(DecodedAudio audio) =>
            [
                .. Enumerable
                    .Range(0, Seconds * 2 - 1)
                    .Select(i => 0.25 + i * 0.5)
                    .Select(time => time >= jumpAt ? time + jumpSeconds : time)
                    .Select(time => new Transient((int)(time * SampleRate), 1f)),
            ];
    }
}
