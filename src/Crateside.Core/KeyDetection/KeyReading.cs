using Crateside.Core.Pairing;

namespace Crateside.Core.KeyDetection;

/// <summary>One detector's key over the audio it was given, or why it has none.</summary>
public abstract record KeyReading
{
    private KeyReading() { }

    /// <summary>A key, with the detector's own confidence: madmom's probability, Essentia's correlation.</summary>
    public sealed record Read(MusicKey Key, double Confidence) : KeyReading;

    public sealed record Failed(string Error) : KeyReading;

    internal static MusicKey KeyOf(int chromatic, Mode mode) =>
        MusicKey.AllKeys.First(key => key.Chromatic == chromatic && key.Mode == mode);
}
