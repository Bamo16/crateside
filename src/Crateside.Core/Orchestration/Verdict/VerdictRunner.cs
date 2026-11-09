using System.Diagnostics;
using System.Globalization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// Turns evidence into a verdict: a headless session writes a verdict file, which crateside
/// checks and records (ADR 0055).
/// </summary>
public sealed class VerdictRunner(
    LedgerStore store,
    WorkingSet workingSet,
    SourceIndex sources,
    JudgeSessions sessions,
    JudgeDetection detection,
    JudgePrompt prompt,
    VerdictCompletion completion,
    JudgeAvailability availability,
    PageLookups pages,
    IOptions<JudgeOptions> options,
    ILogger<VerdictRunner> log
) : IVerdictJudge
{
    public const string VerdictFileName = "verdict.json";

    /// <summary>The pages fetched for the track before its first session, kept beside the evidence.</summary>
    public const string SourcesFileName = PageLookups.SourcesFileName;

    private readonly string _model = options.Value.Model;

    /// <summary>Judges one track: records its verdict, or says why there is none.</summary>
    public Task<VerdictResult> RunAsync(WorkOrder order, CancellationToken ct = default) =>
        RunAsync(order, direction: null, onStep: null, ct);

    public Task<VerdictResult> JudgeAsync(
        WorkOrder order,
        JudgeDirection? direction,
        Action<JudgeStep> onStep,
        CancellationToken ct = default
    ) => RunAsync(order, direction, onStep, ct);

    /// <summary>Judges one track, from the windows the user gave where there is a <paramref name="direction"/> (#116).</summary>
    public async Task<VerdictResult> RunAsync(
        WorkOrder order,
        JudgeDirection? direction,
        Action<JudgeStep>? onStep,
        CancellationToken ct = default
    )
    {
        var step = onStep ?? (_ => { });
        var tally = new SessionTally();

        if (direction is { Audio: null })
            throw new ArgumentException(
                "a run from bars needs its audio found or fetched first",
                nameof(direction)
            );

        if (!workingSet.HasEvidence(order.TrackKey))
        {
            if (direction is null)
                return VerdictResult.Failed("no evidence to judge");

            step(new JudgeStep.DetectingWhole());
            workingSet.Prepare(order.TrackKey);

            var whole = await detection.DetectWholeAsync(
                direction.Audio,
                workingSet.EvidencePathFor(order.TrackKey),
                ct
            );

            if (!whole.IsSuccess)
                return VerdictResult.Failed($"detecting the whole track failed: {whole.Error}");
        }

        var folder = workingSet.Prepare(order.TrackKey);
        var judgeFolder = workingSet.JudgeFolderFor(order.TrackKey);
        var verdictPath = Path.Combine(judgeFolder, VerdictFileName);
        var session = Guid.NewGuid().ToString();
        var audio = direction?.Audio ?? SourceFor(order);
        var given = direction is null
            ? []
            : await detection.DetectAsync(direction.Audio, folder, direction.Windows, step, ct);
        var detected = given;
        RunRecord? withheld = null;
        var resumedWithWindows = false;
        var resumedToWrite = false;

        File.Delete(verdictPath);

        var found = await pages.LookUpAsync(order, ct);
        var lookups = pages.Effective(order.TrackKey, found);

        step(new JudgeStep.Judging());

        var outcome = await sessions.RunAsync(
            ["--session-id", session],
            prompt.Build(
                order,
                verdictPath,
                lookups,
                pages.HeldRun(order),
                direction,
                given,
                audio
            ),
            judgeFolder,
            order,
            tally,
            ct
        );

        while (true)
        {
            string next;

            switch (
                completion.Assess(
                    VerdictFile.Check(verdictPath),
                    order,
                    pages.HeldRun(order),
                    direction,
                    audio,
                    detected,
                    lookups,
                    canResume: !resumedToWrite
                )
            )
            {
                case VerdictCheck.Recordable(var run, var note):
                    LogNotes(run, order);

                    return Record(
                        completion.Complete(
                            VerdictCompletion.WithNote(run, note),
                            order,
                            pages.HeldRun(order),
                            direction,
                            audio,
                            detected
                        ) with
                        {
                            WithheldReasoning = withheld?.Reasoning,
                        },
                        order,
                        tally,
                        found
                    );

                case VerdictCheck.WindowsAsked(var windows, var run)
                    when !resumedWithWindows && audio is not null:
                    resumedWithWindows = true;
                    LogNotes(run with { Windows = windows }, order);
                    step(new JudgeStep.JudgeAsked(windows));
                    var judged = await detection.DetectAsync(
                        audio,
                        folder,
                        windows,
                        step,
                        ct,
                        given.Count
                    );
                    detected = [.. given, .. judged];
                    withheld = completion.Complete(
                        run,
                        order,
                        pages.HeldRun(order),
                        direction,
                        audio,
                        detected
                    );
                    next = JudgePrompt.ForWindows(judged, verdictPath);
                    step(new JudgeStep.Judging());
                    break;

                case VerdictCheck.WindowsAsked(_, var run):
                    LogNotes(run, order);

                    return Record(
                        completion.Complete(
                            run,
                            order,
                            pages.HeldRun(order),
                            direction,
                            audio,
                            detected
                        ) with
                        {
                            WithheldReasoning = withheld?.Reasoning,
                        },
                        order,
                        tally,
                        found
                    );

                case VerdictCheck.Unusable
                    when availability.Unavailable(outcome) is { } unavailable:
                    return unavailable;

                case VerdictCheck.Unusable(var problem) when !resumedToWrite:
                    resumedToWrite = true;
                    next = $"Write the verdict file now, at {verdictPath}. Last time: {problem}.";
                    step(new JudgeStep.Resuming(problem));
                    break;

                case VerdictCheck.Unusable(var problem):
                    return withheld is not null
                        ? Record(withheld, order, tally, found)
                        : VerdictResult.Failed(
                            outcome.Succeeded
                                ? problem
                                : JudgeAvailability.ApiError(outcome)
                                    ?? outcome.LastLine
                                    ?? $"{sessions.FileName} exited with {outcome.ExitCode}"
                        );

                default:
                    throw new UnreachableException();
            }

            File.Delete(verdictPath);
            outcome = await sessions.RunAsync(
                ["--resume", session],
                next,
                judgeFolder,
                order,
                tally,
                ct
            );
        }
    }

    /// <summary>The full mix the order is judged from: the working set's download, else the source the library keeps, by the row's upload or the stated one.</summary>
    public string? SourceFor(WorkOrder order) => sources.PathFor(order);

    private void LogNotes(RunRecord run, WorkOrder order)
    {
        foreach (var note in RunValidation.Notes(run))
            log.LogWarning("Verdict for {TrackKey}: {Note}", order.TrackKey, note);
    }

    /// <param name="lookups">Kept with the run, cited or not (#176).</param>
    private VerdictResult Record(
        RunRecord run,
        WorkOrder order,
        SessionTally tally,
        IReadOnlyList<SiteLookup> lookups
    )
    {
        if (
            store.Record(run with { Lookups = VerdictRecording.Serialized(lookups) })
            is RecordOutcome.Refused(var field, var problem)
        )
            return VerdictResult.Failed($"the ledger refused the verdict: {field}: {problem}");

        VerdictRecording.LinkSwappedSource(store, order);

        return VerdictResult.Succeeded(Summarize(run, _model, tally));
    }

    /// <summary>The verdict a run recorded and what the judge spent on it, for the activity feed (#170).</summary>
    internal static string Summarize(RunRecord run, string model, SessionTally tally)
    {
        var verdict = run.Center is { Name: var name }
            ? $"{name}, {run.Confidence.ToString().ToUpperInvariant()}"
            : $"key withheld, {run.Confidence.ToString().ToUpperInvariant()}";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{verdict}, {"section".ToQuantity(run.Sections.Count)}; {"turn".ToQuantity(tally.Turns)} on {model}, {tally.Seconds:0} s{(tally.CostUsd > 0 ? $", ${tally.CostUsd:0.00}" : string.Empty)}"
        );
    }
}
