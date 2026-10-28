using Crateside.Core.KeyDetection;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>
/// Makes a song's working-set evidence hold a reading from every current detector on keydetect's
/// pinned build before the judge sees it (ADR 0068).
/// </summary>
public sealed class EvidenceCompleter(
    WorkingSet workingSet,
    SourceIndex sources,
    IDetectorSet detectors
)
{
    /// <summary>Null once the evidence is complete; otherwise why it could not be made so.</summary>
    public async Task<string?> CompleteAsync(
        string videoId,
        string? audioPath = null,
        CancellationToken ct = default
    )
    {
        var path = workingSet.EvidencePathFor(videoId);
        var held = await File.ReadAllTextAsync(path, ct);
        var current = await detectors.KeydetectSha256Async(ct);

        if (DetectorBackfill.IsComplete(held, current))
            return null;

        if ((audioPath ?? sources.PathOf(videoId)) is not { } audio || !File.Exists(audio))
            return "its evidence is missing detector readings and no source audio is on disk to read them from";

        if (await detectors.ReadEvidenceAsync(audio, ct) is not EvidenceReading.Read(var fresh))
            return "reading the missing detectors failed";

        if (!DetectorBackfill.IsComplete(fresh, current))
            return "a detector gave no reading, so the evidence stays incomplete";

        if (DetectorBackfill.Refreshed(held, fresh) is not { } refreshed)
            return "its evidence could not be read";

        await File.WriteAllTextAsync($"{path}.tmp", refreshed, ct);
        File.Move($"{path}.tmp", path, overwrite: true);

        return null;
    }
}
