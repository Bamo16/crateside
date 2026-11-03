namespace Crateside.Core.Library;

/// <summary>The library's songs by how the starter warp compared with the finished one (ADR 0075).</summary>
public sealed record StarterWarpCounts(IReadOnlyList<StarterBucket> Buckets, int NotCompared)
{
    public int Compared => Buckets.Sum(bucket => bucket.Songs.Count);

    public static StarterWarpCounts From(IReadOnlyCollection<LibrarySong> songs) =>
        new(
            [
                .. Enum.GetValues<StarterFate>()
                    .Select(fate => new StarterBucket(
                        fate,
                        [
                            .. songs
                                .Where(song => song.Starter == fate)
                                .OrderBy(song => song.Name, StringComparer.OrdinalIgnoreCase),
                        ]
                    )),
            ],
            songs.Count(song => song.Starter is null)
        );
}

public sealed record StarterBucket(StarterFate Fate, IReadOnlyList<LibrarySong> Songs);
