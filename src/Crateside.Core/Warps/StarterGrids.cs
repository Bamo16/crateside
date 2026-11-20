using Crateside.Core.Beats;

namespace Crateside.Core.Warps;

/// <summary>What a starter takes from an upload's reading.</summary>
public interface IStarterGrids
{
    /// <summary>Null before the upload's reference file is read, or when its reading drifts.</summary>
    StarterGrid? Of(SiblingFile cut);

    /// <summary>Why <see cref="Of"/> gave nothing for this reference file.</summary>
    string WhyNone(SiblingFile cut) => StarterGrid.WhyNone(null);
}

/// <summary>Starter grids from the readings beatdetect has kept.</summary>
public sealed class StarterGrids(BeatReader beats) : IStarterGrids
{
    public StarterGrid? Of(SiblingFile cut) =>
        StarterGrid.Of(beats.ReadFor(cut.SourceTrackKey, cut.Path));

    public string WhyNone(SiblingFile cut) =>
        StarterGrid.WhyNone(beats.ReadFor(cut.SourceTrackKey, cut.Path));
}
