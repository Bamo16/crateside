using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Plans;
using Crateside.Core.Warps;

namespace Crateside.Core.Tracks;

/// <summary>What is read off a track's sample files: their presets, Ableton's tags, tempo, beats and warps.</summary>
public sealed record SampleReadings
{
    public static SampleReadings None { get; } = new();

    public PresetCatalog Presets { get; init; } = PresetCatalog.Empty;

    public FolderInfo? LibraryTags { get; init; }

    public Func<string, WarpTempo?>? TempoOf { get; init; }

    public Func<string?, string, BeatReading?>? BeatsOf { get; init; }

    public Func<string, WrittenWarp?>? WrittenWarp { get; init; }

    /// <summary>The integrated loudness kept for a file, or null when none is.</summary>
    public Func<string, double?>? LufsOf { get; init; }
}
