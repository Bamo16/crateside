using Crateside.Core.Beats;

namespace Crateside.Core.Warps;

/// <summary>What an auto warp takes from an upload's reading.</summary>
public interface IAutoWarpGrids
{
    /// <summary>Null before the upload's reference file is read, or when its reading drifts.</summary>
    AutoWarpGrid? Of(SiblingFile cut);

    /// <summary>Why <see cref="Of"/> gave nothing for this reference file.</summary>
    string WhyNone(SiblingFile cut) => AutoWarpGrid.WhyNone(null);
}

/// <summary>Auto warp grids from the readings beatdetect has kept.</summary>
public sealed class AutoWarpGrids(BeatReader beats) : IAutoWarpGrids
{
    public AutoWarpGrid? Of(SiblingFile cut) =>
        AutoWarpGrid.Of(beats.ReadFor(cut.SourceTrackKey, cut.Path));

    public string WhyNone(SiblingFile cut) =>
        AutoWarpGrid.WhyNone(beats.ReadFor(cut.SourceTrackKey, cut.Path));
}
