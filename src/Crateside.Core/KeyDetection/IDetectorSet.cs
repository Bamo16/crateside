using Crateside.Core.Ledger;

namespace Crateside.Core.KeyDetection;

public interface IDetectorSet
{
    /// <summary>Detects over the audio, or one window of it, and writes the evidence the judge reads.</summary>
    Task<DetectionOutcome> DetectAsync(
        string audioPath,
        RunWindow? window,
        string evidencePath,
        CancellationToken ct = default
    );

    /// <summary>madmom and edma alone, on the CPU.</summary>
    Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
        string audioPath,
        RunWindow? window,
        CancellationToken ct = default
    );
}
