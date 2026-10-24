using Crateside.Core.Beats;

namespace Crateside.Core.Warps;

/// <summary>What a starter takes from an upload's reading.</summary>
public interface IStarterGrids
{
    /// <summary>Null before the upload's reference file is read, or when its reading drifts.</summary>
    StarterGrid? Of(SiblingFile cut);
}

/// <summary>Starter grids from the readings beatdetect has kept.</summary>
public sealed class StarterGrids(BeatReader beats) : IStarterGrids
{
    public StarterGrid? Of(SiblingFile cut) =>
        StarterGrid.Of(beats.ReadFor(cut.SourceVideoId, cut.Path));
}
