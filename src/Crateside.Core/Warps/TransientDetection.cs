using AbleKit.Analysis;

namespace Crateside.Core.Warps;

public interface ITransientDetector
{
    /// <summary>The detector and its version, recorded with what it writes, so a later version can tell its output apart.</summary>
    string Version { get; }

    IReadOnlyList<Transient> Detect(DecodedAudio audio);
}

/// <summary>Writes no transients: Live then shows none and never detects its own.</summary>
public sealed class NoTransients : ITransientDetector
{
    public string Version => "none";

    public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [];
}
