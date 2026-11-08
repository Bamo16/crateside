namespace Crateside.Core.Marks;

/// <summary>
/// A song marked as not fit for measuring detectors: all of it, or the stretches given (ADR 0085).
/// </summary>
public sealed record MeasuringExclusion
{
    /// <summary>The ledger's song identity, or the upload's video ID for a song it never judged.</summary>
    public required string Song { get; init; }

    /// <summary>Why the whole track is out; null when only the stretches are.</summary>
    public string? Reason { get; init; }

    public IReadOnlyList<MeasuringStretch> Stretches { get; init; } = [];

    public bool WholeTrack => Reason is not null;
}

/// <summary>One stretch of a track that detectors are not judged on, in seconds of the source file.</summary>
public sealed record MeasuringStretch
{
    public required double StartSeconds { get; init; }

    public required double EndSeconds { get; init; }

    /// <summary>The range as the maintainer typed it: clip bars with a warp, m:ss without.</summary>
    public required string Typed { get; init; }

    public required string Reason { get; init; }
}
