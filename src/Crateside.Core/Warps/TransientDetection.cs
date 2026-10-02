using AbleKit.Analysis;

namespace Crateside.Core.Warps;

public interface ITransientDetector
{
    IReadOnlyList<Transient> Detect(DecodedAudio audio);
}

/// <summary>Writes no transients: Live then shows none and never detects its own.</summary>
public sealed class NoTransients : ITransientDetector
{
    public IReadOnlyList<Transient> Detect(DecodedAudio audio) => [];
}
