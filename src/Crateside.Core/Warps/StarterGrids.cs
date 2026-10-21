using Crateside.Core.Beats;

namespace Crateside.Core.Warps;

/// <summary>What a starter takes from an audio file's reading.</summary>
public interface IStarterGrids
{
    /// <summary>Null before the file is read, or when its reading drifts.</summary>
    StarterGrid? Of(string audioPath);
}

/// <summary>Starter grids from the readings beatdetect has kept.</summary>
public sealed class StarterGrids(BeatReader beats) : IStarterGrids
{
    public StarterGrid? Of(string audioPath) => StarterGrid.Of(beats.Read(audioPath));
}
