using System.Diagnostics;
using System.Globalization;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>Detects over one stretch of a track, for windows the judge or the user asked for (ADR 0055).</summary>
public interface IWindowDetector
{
    Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    );

    /// <summary>The whole track, for a run whose working set no longer holds its evidence.</summary>
    Task<EvidenceResult> DetectWholeAsync(
        string audioPath,
        string evidencePath,
        CancellationToken ct = default
    );
}

/// <summary>
/// Produces the evidence for one track: runs the detectors over audio crateside already holds
/// (ADR 0019).
/// </summary>
public sealed class EvidenceRunner(
    WorkingSet workingSet,
    IDetectorSet detectors,
    PageLookups? pages = null
) : IWindowDetector
{
    /// <summary>Detects, then looks the song up on the sites; a detection already on file is not made again, and its lookup is made if none is kept (ADR 0096).</summary>
    public async Task<EvidenceResult> RunAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (workingSet.HasEvidence(order.TrackKey))
            return await WithPagesAsync(
                EvidenceResult.Succeeded(
                    workingSet.EvidencePathFor(order.TrackKey),
                    "evidence was already on file"
                ),
                order,
                ct
            );

        if (!workingSet.TryGetSource(order.TrackKey, out var audio))
            return EvidenceResult.Failed("no audio in the working set to detect over");

        return await WithPagesAsync(
            await DetectAsync(audio, window: null, workingSet.EvidencePathFor(order.TrackKey), ct),
            order,
            ct
        );
    }

    private async Task<EvidenceResult> WithPagesAsync(
        EvidenceResult detected,
        WorkOrder order,
        CancellationToken ct
    )
    {
        if (pages is null || !detected.IsSuccess)
            return detected;

        var lookups = await pages.LookUpForEvidenceAsync(order, ct);

        return detected with
        {
            Summary = string.Join(
                "; ",
                new[] { detected.Summary, PagesSummary(lookups) }.Where(part =>
                    part is { Length: > 0 }
                )
            ),
        };
    }

    /// <summary>What the sites gave, in a line for the activity feed: <c>pages: Hooktheory 1, Musicnotes none, Ultimate Guitar not reached</c>.</summary>
    internal static string PagesSummary(IReadOnlyList<SiteLookup> lookups) =>
        lookups is []
            ? string.Empty
            : "pages: "
                + string.Join(
                    ", ",
                    lookups.Select(lookup =>
                        $"{lookup.Site} "
                        + lookup.Status switch
                        {
                            LookupStatus.Found => lookup.Pages.Count.ToString(
                                CultureInfo.InvariantCulture
                            ),
                            LookupStatus.NotFound => "none",
                            LookupStatus.Unreachable => "not reached",
                            _ => "not looked up",
                        }
                    )
                );

    public Task<EvidenceResult> DetectWindowAsync(
        string audioPath,
        RunWindow window,
        string evidencePath,
        CancellationToken ct = default
    ) => DetectAsync(audioPath, window, evidencePath, ct);

    public Task<EvidenceResult> DetectWholeAsync(
        string audioPath,
        string evidencePath,
        CancellationToken ct = default
    ) => DetectAsync(audioPath, window: null, evidencePath, ct);

    private async Task<EvidenceResult> DetectAsync(
        string audioPath,
        RunWindow? window,
        string evidencePath,
        CancellationToken ct
    ) =>
        await detectors.DetectAsync(audioPath, window, evidencePath, ct) switch
        {
            DetectionOutcome.Written(var path) written => EvidenceResult.Succeeded(
                path,
                written.Summary
            ),
            DetectionOutcome.Failed(var error) => EvidenceResult.Failed(error),
            _ => throw new UnreachableException(),
        };
}

/// <summary>How one evidence run turned out.</summary>
public sealed record EvidenceResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>Where the machine layer landed. Null unless it succeeded.</summary>
    public string? EvidencePath { get; init; }

    public string? Error { get; init; }

    /// <summary>What the detectors read, in a line for the activity feed (#170). Null unless it succeeded.</summary>
    public string? Summary { get; init; }

    public static EvidenceResult Succeeded(string evidencePath, string? summary = null) =>
        new()
        {
            IsSuccess = true,
            EvidencePath = evidencePath,
            Summary = summary,
        };

    public static EvidenceResult Failed(string error) => new() { IsSuccess = false, Error = error };
}
