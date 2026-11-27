using System.Text.Json.Serialization;

namespace Crateside.Core.SourceOffsets;

[JsonConverter(typeof(JsonStringEnumConverter<OffsetVerdict>))]
public enum OffsetVerdict
{
    /// <summary>The added source lines up with the mix by one shift (ADR 0111, rule 9).</summary>
    Aligned,

    /// <summary>Two or more runs on distinct offsets: cut differently from the mix (rule 11).</summary>
    Edit,

    /// <summary>Runs at another speed than the mix (rules 10 and 12).</summary>
    Drift,

    /// <summary>Too few confident windows, or none sharing an offset (rule 12).</summary>
    NotTheSameRecording,
}

[JsonConverter(typeof(JsonStringEnumConverter<WindowState>))]
public enum WindowState
{
    /// <summary>Confident, and on the offset the verdict rests on.</summary>
    Voting,

    /// <summary>Confident, on another offset: a run of an edit, or material the mix repeats.</summary>
    Elsewhere,

    /// <summary>Under the threshold: unmatched content, never disagreement.</summary>
    Unmatched,

    Silent,
}

/// <param name="Start">Seconds into the added source.</param>
/// <param name="Offset">Samples; the added source's sample n matches the mix's sample n + this.</param>
/// <param name="Psr">The peak's height over that window's own correlation noise.</param>
public sealed record OffsetWindow(double Start, int Offset, double Psr, WindowState State);

public sealed record OffsetSpan(double From, double To);

/// <summary>A stretch of the added source that sits on one offset.</summary>
public sealed record OffsetRun(double From, double To, int Offset);

/// <summary>What measuring an added source against its mix found (ADR 0111, rule 3).</summary>
public sealed record OffsetReading
{
    public required OffsetVerdict Verdict { get; init; }

    public required int SampleRate { get; init; }

    public required double WindowSeconds { get; init; }

    /// <summary>The shift in samples; set for an aligned source.</summary>
    public int? Offset { get; init; }

    public IReadOnlyList<OffsetWindow> Windows { get; init; } = [];

    /// <summary>The runs of an edit, in order.</summary>
    public IReadOnlyList<OffsetRun> Runs { get; init; } = [];

    /// <summary>Spans of the added source no confident window of it matches the mix on.</summary>
    public IReadOnlyList<OffsetSpan> OnlyInSource { get; init; } = [];

    /// <summary>Spans of the mix no confident window lands on.</summary>
    public IReadOnlyList<OffsetSpan> OnlyInMix { get; init; } = [];

    /// <summary>Spans matching the mix at another place: material the mix repeats.</summary>
    public IReadOnlyList<OffsetSpan> Repeated { get; init; } = [];

    /// <summary>Parts per million the added source runs fast against the mix, negative for slow; set for a drift.</summary>
    public double? DriftPpm { get; init; }

    /// <summary>The known speed change that made it align, as <c>1000/1001</c>; set for a drift read after a retry.</summary>
    public string? SpeedFactor { get; init; }

    [JsonIgnore]
    public int ConfidentWindows =>
        Windows.Count(window => window.State is WindowState.Voting or WindowState.Elsewhere);

    [JsonIgnore]
    public int AwakeWindows => Windows.Count(window => window.State is not WindowState.Silent);

    [JsonIgnore]
    public double OffsetMilliseconds => Offset is { } samples ? samples * 1000.0 / SampleRate : 0;
}
