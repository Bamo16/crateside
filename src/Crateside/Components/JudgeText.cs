using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Tempo;

namespace Crateside.Components;

/// <summary>How a judgment from these bars is worded, in the band, the warp view and Why this key (#116).</summary>
public static class JudgeText
{
    public const string Leave =
        "You can leave. It carries on, and the Dashboard's Judging card shows it.";

    /// <summary><c>bars 33–48, 82–96</c>, or times where the windows were not given in bars.</summary>
    public static string Spans(IEnumerable<RunWindow> windows) =>
        windows.ToList() switch
        {
            [] => string.Empty,
            var all when all.All(window => window.Bars is not null) =>
                $"bars {string.Join(", ", all.Select(window => $"{window.Bars!.First}–{window.Bars.Last}"))}",
            var all => string.Join(", ", all.Select(Time)),
        };

    public static string Span(RunWindow window) =>
        window.Bars is { } bars ? $"bars {bars.First}–{bars.Last}" : Time(window);

    public static string Time(RunWindow window) =>
        $"{TempoFormat.Clock(window.Start)}–{TempoFormat.Clock(window.End)}";

    /// <summary>Where the run has got to, as the line under the steps says it.</summary>
    public static string Step(BarRun run, Func<RunWindow, string> span) =>
        run.Step switch
        {
            null => "Waiting for the judgment already under way to finish",
            JudgeStep.DetectingWhole =>
                "Detecting the whole track first: crateside no longer held its evidence",
            JudgeStep.DetectingWindow(var number, var of, var window)
                when run.JudgeAsked.Count > 0 =>
                $"Detecting the judge's window {number} of {of} · {span(window)}",
            JudgeStep.DetectingWindow(var number, var of, var window) =>
                $"Detecting window {number} of {of} · {span(window)}",
            JudgeStep.JudgeAsked(var asked) =>
                $"The judge asked for {string.Join(", ", asked.Select(span))}",
            JudgeStep.Resuming => "Resuming once: the judge ended without a verdict.",
            _ when run.JudgeAsked.Count > 0 => "Judging again, with the judge's windows",
            _ => "Judging",
        };

    /// <summary>The step as the band's sub-line says it, after the spans: <c>detecting window 2 of 2</c>.</summary>
    public static string Brief(BarRun run) =>
        run.Step switch
        {
            null => "waiting its turn",
            JudgeStep.DetectingWhole => "detecting the whole track",
            JudgeStep.DetectingWindow(var number, var of, _) when run.JudgeAsked.Count > 0 =>
                $"detecting the judge's window {number} of {of}",
            JudgeStep.DetectingWindow(var number, var of, _) =>
                $"detecting window {number} of {of}",
            JudgeStep.JudgeAsked => "the judge asked for windows of its own",
            JudgeStep.Resuming => "resuming once",
            _ => "judging",
        };

    /// <summary>0 while detecting, 1 while judging.</summary>
    public static int Phase(BarRun run) =>
        run.Step is JudgeStep.Judging or JudgeStep.Resuming ? 1 : 0;

    /// <summary>The Confidence cell's reason for inviting a run, or null where it should not.</summary>
    public static string? Invitation(KeyConfidence confidence, RunEvidence? run)
    {
        var keys =
            run?.Algorithms.Select(algorithm => algorithm.Key)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            ?? 0;
        var split = keys switch
        {
            >= 3 => "The detectors split three ways over the whole file.",
            2 => "The detectors split two ways over the whole file.",
            _ => null,
        };

        return (confidence, split) switch
        {
            (KeyConfidence.Withheld, _) =>
                $"{split ?? "Withheld over the whole file."} Name the bars where the key is clear.",
            (KeyConfidence.Low, { } reason) => $"{reason} Name the bars where the key is clear.",
            _ => null,
        };
    }
}
