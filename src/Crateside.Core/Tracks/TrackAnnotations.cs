using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Imports;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Tracks;

/// <summary>What is recorded about a track beyond its files, which a row carries alongside them.</summary>
public sealed record TrackAnnotations
{
    public static TrackAnnotations None { get; } = new();

    public IReadOnlyDictionary<string, ResolvedName> ResolvedNames { get; init; } =
        new Dictionary<string, ResolvedName>();

    /// <summary>How each retained download turned out, by video ID — what a row is parked over.</summary>
    public IReadOnlyDictionary<string, Acquisition> Acquisitions { get; init; } =
        new Dictionary<string, Acquisition>();

    /// <summary>What the source of a track not taken from the playlist said about its release, by track key.</summary>
    public IReadOnlyDictionary<string, Release> Releases { get; init; } =
        new Dictionary<string, Release>();

    public IReadOnlyList<Deletion> Deletions { get; init; } = [];

    public IReadOnlyDictionary<string, GenreRecord> Genres { get; init; } =
        new Dictionary<string, GenreRecord>();

    public IReadOnlyDictionary<string, FamiliarityRecord> Familiarity { get; init; } =
        new Dictionary<string, FamiliarityRecord>();

    public IReadOnlyDictionary<string, TrackMarkRecord> Marks { get; init; } =
        new Dictionary<string, TrackMarkRecord>();
}
