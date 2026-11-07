using Crateside.Core.Ableton;

namespace Crateside.Core.Library;

/// <summary>How many warps of one shape beatdetect flagged as drifting, of those it could judge (#75).</summary>
public sealed record DriftCount(TempoShape Shape, int Flagged, int Judged)
{
    public static IReadOnlyList<DriftCount> From(IEnumerable<LibrarySong> songs)
    {
        var judged = songs
            .Where(song => song is { Tempo: not null, Beats.Drift: not null })
            .ToList();

        return
        [
            .. Enum.GetValues<TempoShape>()
                .Select(shape =>
                {
                    var warps = judged.Where(song => song.Tempo!.Shape == shape).ToList();

                    return new DriftCount(
                        shape,
                        warps.Count(song => song.Beats!.Drifts),
                        warps.Count
                    );
                }),
        ];
    }
}
