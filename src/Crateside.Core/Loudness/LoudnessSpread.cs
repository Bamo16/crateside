using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.Loudness;

/// <summary>How large the clip gains of the instrumentals and the vocals are, as <see cref="ClipGains"/> wants them (ADR 0059).</summary>
public sealed record LoudnessSpread(
    LoudnessOptions Options,
    LoudnessGroup Instrumentals,
    LoudnessGroup Vocals
)
{
    public static LoudnessSpread From(
        IEnumerable<string> samples,
        LoudnessReader reader,
        ClipGains gains
    )
    {
        var options = gains.Options;
        Dictionary<string, Tally> tallies = new()
        {
            [nameof(SampleType.Instrumental)] = new(),
            [nameof(SampleType.Vocal)] = new(),
        };

        foreach (var path in samples)
        {
            var preset = StemTagReader.PresetOf(path);

            if (
                TaggingRunner.TypeFor(preset, StemTagReader.Read(path)?.Provenance?.Type)
                    is not { } kind
                || StagedFile.IsWarpGuidePreset(preset)
            )
                continue;

            var tally = tallies[kind];

            switch (reader.Read(path))
            {
                case null:
                    tally.NotRead++;
                    break;
                case { IntegratedLufs: null }:
                    tally.Unreadable++;
                    break;
                case { IntegratedLufs: { } lufs } when lufs < options.FloorLufs:
                    tally.BelowFloor++;
                    break;
                default:
                    if (gains.For(path) is { } gain)
                        tally.Levelled.Add(
                            new LoudnessSample(NameOf(path, preset ?? kind), 20 * Math.Log10(gain))
                        );
                    break;
            }
        }

        return new LoudnessSpread(
            options,
            tallies[nameof(SampleType.Instrumental)].Group(),
            tallies[nameof(SampleType.Vocal)].Group()
        );
    }

    private static string NameOf(string path, string label) =>
        $"{StemFileName.WithoutPreset(path)} ({label})";

    private sealed class Tally
    {
        public List<LoudnessSample> Levelled { get; } = [];
        public int BelowFloor { get; set; }
        public int Unreadable { get; set; }
        public int NotRead { get; set; }

        public LoudnessGroup Group() => new(Levelled, BelowFloor, Unreadable, NotRead);
    }
}

public sealed record LoudnessSample(string Name, double GainDb);

public sealed record LoudnessBin(int FromDb, int Count);

public sealed record LoudnessGroup(
    IReadOnlyList<LoudnessSample> Levelled,
    int BelowFloor,
    int Unreadable,
    int NotRead
)
{
    private const int Extremes = 5;

    public int Count => Levelled.Count;

    public double? Median => Percentile(0.5);

    public double? Low => Percentile(0.05);

    public double? High => Percentile(0.95);

    /// <summary>Every 1 dB bin from the lowest gain to the highest, 0 dB's included, empty ones too.</summary>
    public IReadOnlyList<LoudnessBin> Bins
    {
        get
        {
            if (Levelled.Count is 0)
                return [];

            var counts = Levelled
                .GroupBy(sample => (int)Math.Floor(sample.GainDb))
                .ToDictionary(group => group.Key, group => group.Count());
            var from = Math.Min(0, counts.Keys.Min());
            var to = Math.Max(0, counts.Keys.Max());

            return
            [
                .. Enumerable
                    .Range(from, to - from + 1)
                    .Select(db => new LoudnessBin(db, counts.GetValueOrDefault(db))),
            ];
        }
    }

    /// <summary>The biggest boosts, largest first.</summary>
    public IReadOnlyList<LoudnessSample> Boosts =>
        [
            .. Levelled
                .Where(sample => sample.GainDb > 0)
                .OrderByDescending(sample => sample.GainDb)
                .Take(Extremes),
        ];

    /// <summary>The biggest cuts, largest first.</summary>
    public IReadOnlyList<LoudnessSample> Cuts =>
        [
            .. Levelled
                .Where(sample => sample.GainDb < 0)
                .OrderBy(sample => sample.GainDb)
                .Take(Extremes),
        ];

    public int Beyond(double db) => Levelled.Count(sample => Math.Abs(sample.GainDb) > db);

    private double? Percentile(double fraction)
    {
        if (Levelled.Count is 0)
            return null;

        var sorted = Levelled.Select(sample => sample.GainDb).Order().ToArray();
        var rank = fraction * (sorted.Length - 1);
        var below = (int)Math.Floor(rank);
        var above = (int)Math.Ceiling(rank);

        return sorted[below] + (sorted[above] - sorted[below]) * (rank - below);
    }
}
