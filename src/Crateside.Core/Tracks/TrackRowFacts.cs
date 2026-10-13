using Crateside.Core.Plans;

namespace Crateside.Core.Tracks;

/// <summary>What a row knows about itself that adding a source needs (ADR 0014).</summary>
public sealed record TrackRowFacts
{
    public required string VideoId { get; init; }

    /// <summary>The ledger identity every upload of this track resolves to.</summary>
    public string? Song { get; init; }

    /// <summary>The key an added source inherits, or null when there is none to inherit.</summary>
    public string? Key { get; init; }

    public required StemPlan Plan { get; init; }

    /// <summary>Every upload this track already speaks for, so none is added twice.</summary>
    public required IReadOnlyList<string> Uploads { get; init; }

    /// <summary>The row exists only because the library holds files for it.</summary>
    public bool IsLibraryOnly { get; init; }

    /// <summary>
    /// Whether the ledger has ever judged this track, even to a verdict that left
    /// <see cref="Key"/> null.
    /// </summary>
    public bool HasVerdict { get; init; }

    public bool HasUpload { get; init; } = true;

    /// <summary>Labels already in use, which a new one may not repeat.</summary>
    public IReadOnlyList<string> Labels => [.. Plan.AddedSources.Select(source => source.Label)];
}
