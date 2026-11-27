namespace Crateside.Core.Library;

/// <summary>The library's songs by how the auto warp compared with the finished one (ADR 0075).</summary>
public sealed record AutoWarpCounts(
    IReadOnlyList<AutoWarpBucket> Buckets,
    int NotCompared,
    int KeptWithMeter = 0
)
{
    public int Compared => Buckets.Sum(bucket => bucket.Songs.Count);

    public static AutoWarpCounts From(IReadOnlyCollection<LibrarySong> songs) =>
        new(
            [
                .. Enum.GetValues<AutoWarpFate>()
                    .Select(fate => new AutoWarpBucket(
                        fate,
                        [
                            .. songs
                                .Where(song => song.AutoWarp == fate)
                                .OrderBy(song => song.Name, StringComparer.OrdinalIgnoreCase),
                        ]
                    )),
            ],
            songs.Count(song => song.AutoWarp is null),
            songs.Count(song => song.AutoWarp is AutoWarpFate.Kept && song.Meter.Count > 0)
        );
}

public sealed record AutoWarpBucket(AutoWarpFate Fate, IReadOnlyList<LibrarySong> Songs);
