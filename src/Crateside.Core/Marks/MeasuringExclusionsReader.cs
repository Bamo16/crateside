using System.Text.Json.Serialization;
using Crateside.Core.Tracks;

namespace Crateside.Core.Marks;

/// <summary>Every track marked as not fit for measuring detectors, by the video ID the harnesses know it by (ADR 0085).</summary>
public sealed record MeasuringExclusionsView(IReadOnlyList<TrackExclusion> Tracks);

/// <summary>One track's exclusion: all of it, or the stretches given, in seconds of the source file.</summary>
public sealed record TrackExclusion
{
    [JsonPropertyName("videoId")]
    public required string TrackKey { get; init; }

    public required string Song { get; init; }

    public required bool WholeTrack { get; init; }

    /// <summary>Why the whole track is out; null when only stretches are.</summary>
    public string? Reason { get; init; }

    public IReadOnlyList<MeasuringStretch> Stretches { get; init; } = [];
}

/// <summary>Reads the exclusions against the rows as last built, which say which tracks a song is.</summary>
public sealed class MeasuringExclusionsReader(
    TracksService tracks,
    MeasuringExclusionStore exclusions
)
{
    public MeasuringExclusionsView Read()
    {
        var marked = exclusions.Read();

        return new([
            .. (tracks.Current?.Rows ?? [])
                .OrderBy(row => row.TrackKey, StringComparer.Ordinal)
                .Select(row =>
                    marked.TryGetValue(row.Song, out var exclusion)
                        ? new TrackExclusion
                        {
                            TrackKey = row.TrackKey,
                            Song = exclusion.Song,
                            WholeTrack = exclusion.WholeTrack,
                            Reason = exclusion.Reason,
                            Stretches = exclusion.Stretches,
                        }
                        : null
                )
                .OfType<TrackExclusion>(),
        ]);
    }
}
