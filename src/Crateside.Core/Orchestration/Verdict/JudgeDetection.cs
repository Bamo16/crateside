using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Has crateside detect over the whole track or over windows of it, for a judge that asks.</summary>
public sealed class JudgeDetection(IWindowDetector detector)
{
    internal Task<EvidenceResult> DetectWholeAsync(
        string audio,
        string evidencePath,
        CancellationToken ct
    ) => detector.DetectWholeAsync(audio, evidencePath, ct);

    /// <param name="first">How many windows this run has already detected, so their files are kept.</param>
    internal async Task<List<DetectedWindow>> DetectAsync(
        string audio,
        string folder,
        IReadOnlyList<RunWindow> windows,
        Action<JudgeStep> step,
        CancellationToken ct,
        int first = 0
    )
    {
        List<DetectedWindow> detected = [];

        foreach (var (window, index) in windows.Select((window, index) => (window, index)))
        {
            var path = Path.Combine(folder, $"evidence.window{first + index + 1}.json");

            step(new JudgeStep.DetectingWindow(index + 1, windows.Count, window));
            File.Delete(path);

            var result = await detector.DetectWindowAsync(audio, window, path, ct);

            detected.Add(
                result.IsSuccess
                    ? new(window, File.ReadAllText(path), null)
                    : new(window, null, result.Error)
            );
        }

        return detected;
    }
}

internal sealed record DetectedWindow(RunWindow Window, string? Evidence, string? Error);
