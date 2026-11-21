using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;

namespace Crateside.Core.SourceOffsets;

/// <summary>An added source's file and the mix it is measured against.</summary>
public sealed record SourceOffsetPair(
    string TrackKey,
    string SourceKey,
    string Label,
    string MixPath,
    string SourcePath
)
{
    /// <summary>Every added source in the rows whose track's mix is on disk (ADR 0111, rule 1).</summary>
    public static IReadOnlyList<SourceOffsetPair> Find(
        IReadOnlyList<TrackRow> rows,
        SourceIndex sources
    ) =>
        [
            .. rows.Where(row => row is { HasUpload: true, Plan.AddedSources.Count: > 0 })
                .SelectMany(row =>
                    sources.PathOf(row.Uploads[0].TrackKey) is { } mix
                        ? row
                            .Samples.Where(sample =>
                                sample is { Kind: Plans.StemKind.Source, SourceTrackKey: { } key }
                                && row.IsAddedSource(key)
                                && sample.Path is { Length: > 0 }
                            )
                            .Select(sample => new SourceOffsetPair(
                                row.TrackKey,
                                sample.SourceTrackKey!,
                                sample.Label,
                                mix,
                                sample.Path
                            ))
                        : []
                ),
        ];
}

/// <summary>Measures each added source against its mix, once, and again only when either file changes (ADR 0111, rules 1 and 2).</summary>
public sealed class SourceOffsetPass(
    IAudioDecoder decoder,
    SourceOffsetStore store,
    TimeProvider clock
)
{
    /// <summary>A longer file is not measured: its transform would not fit comfortably in memory.</summary>
    private const double MaxSeconds = 600;

    private readonly HashSet<string> _measuring = [with(StringComparer.Ordinal)];
    private readonly HashSet<(string, FileStamp, FileStamp)> _unmeasurable = [];
    private readonly Lock _lock = new();

    /// <summary>Whether this added source is being measured now.</summary>
    public bool IsMeasuring(string sourceKey)
    {
        lock (_lock)
        {
            return _measuring.Contains(sourceKey);
        }
    }

    /// <summary>The added sources measured; those already measured as they stand are left.</summary>
    public async Task<IReadOnlyList<SourceOffsetPair>> RunAsync(
        IReadOnlyList<SourceOffsetPair> pairs,
        CancellationToken ct
    )
    {
        List<SourceOffsetPair> measured = [];

        foreach (var pair in pairs)
        {
            ct.ThrowIfCancellationRequested();

            if (await SettleAsync(pair, ct))
                measured.Add(pair);
        }

        return measured;
    }

    internal async Task<bool> SettleAsync(SourceOffsetPair pair, CancellationToken ct)
    {
        if (
            FileStamp.Of(pair.MixPath) is not { } mix
            || FileStamp.Of(pair.SourcePath) is not { } source
        )
            return false;

        if (
            store.Of(pair.SourceKey) is { } kept
            && kept.Mix.IsSameFileAs(mix)
            && kept.Source.IsSameFileAs(source)
        )
        {
            if (kept.Mix != mix || kept.Source != source)
                store.Save(pair.SourceKey, kept with { Mix = mix, Source = source });

            return false;
        }

        lock (_lock)
        {
            if (_unmeasurable.Contains((pair.SourceKey, mix, source)))
                return false;

            _measuring.Add(pair.SourceKey);
        }

        try
        {
            if (
                await decoder.DecodeAsync(pair.MixPath, ct) is not { } mixAudio
                || await decoder.DecodeAsync(pair.SourcePath, ct) is not { } sourceAudio
                || mixAudio.SampleRate != sourceAudio.SampleRate
                || mixAudio.Frames > MaxSeconds * mixAudio.SampleRate
                || sourceAudio.Frames > MaxSeconds * sourceAudio.SampleRate
            )
            {
                lock (_lock)
                    _unmeasurable.Add((pair.SourceKey, mix, source));

                return false;
            }

            var reading = await Task.Run(
                () =>
                    OffsetMeasurer.Measure(
                        Mono(mixAudio),
                        Mono(sourceAudio),
                        mixAudio.SampleRate,
                        ct
                    ),
                ct
            );

            store.Save(
                pair.SourceKey,
                new SourceOffsetRecord(pair.TrackKey, mix, source, reading, clock.GetUtcNow())
            );

            return true;
        }
        finally
        {
            lock (_lock)
                _measuring.Remove(pair.SourceKey);
        }
    }

    internal static float[] Mono(DecodedAudio audio)
    {
        if (audio.ChannelCount is 1)
            return audio.Samples;

        var mono = new float[audio.Frames];

        for (var i = 0; i < mono.Length; i++)
        {
            var sum = 0f;

            for (var channel = 0; channel < audio.ChannelCount; channel++)
                sum += audio.Samples[(i * audio.ChannelCount) + channel];

            mono[i] = sum / audio.ChannelCount;
        }

        return mono;
    }
}
