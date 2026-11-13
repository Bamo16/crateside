using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Playlist;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Checks the run a session wrote against the evidence and the rules, and adds what only crateside knows.</summary>
public sealed class VerdictCompletion(
    WorkingSet workingSet,
    IOptions<JudgeOptions> options,
    TimeProvider time
)
{
    private const string HighNeeds =
        "HIGH needs backed detectors, a tonic check in favor and a human source that agrees, "
        + "with no authoritative source stating another key";

    private readonly JudgeOptions _options = options.Value;

    /// <summary>
    /// A recordable run the ledger would refuse, or whose key the evidence never held, is unusable,
    /// so the session is told why; a HIGH without its backing is capped once the session has had its resume (ADR 0069).
    /// </summary>
    internal VerdictCheck Assess(
        VerdictCheck check,
        WorkOrder order,
        LedgerRun? held,
        JudgeDirection? direction,
        string? audio,
        IReadOnlyList<DetectedWindow> detected,
        IReadOnlyList<SiteLookup> lookups,
        bool canResume
    )
    {
        if (check is not VerdictCheck.Recordable(var run, _))
            return check;

        var complete = RunValidation.Normalize(
            Complete(run, order, held, direction, audio, detected)
        );

        if (RunValidation.Refusal(complete) is { } refusal)
            return new VerdictCheck.Unusable($"{refusal.Field}: {refusal.Problem}");

        if (RecordTimeCheck.UnfoundKey(complete, lookups) is { } unfound)
            return new VerdictCheck.Unusable(unfound);

        if (RecordTimeCheck.PrimaryOutsideWindows(complete, _options.Ear) is { } outside)
            return new VerdictCheck.Unusable(outside);

        if (RecordTimeCheck.LeansOnHypothesis(complete, _options.Ear))
            return new VerdictCheck.Recordable(
                run with
                {
                    Confidence = KeyConfidence.Low,
                },
                "crateside capped this verdict at LOW: nothing but sections no source, window or "
                    + "detector placed supports it."
            );

        if (RecordTimeCheck.MissingForHigh(complete, _options.Ear, lookups) is not { } missing)
            return check;

        return canResume
            ? new VerdictCheck.Unusable(
                $"confidence: {HighNeeds}; missing {missing}. "
                    + "Write the confidence the evidence supports, or add what is missing."
            )
            : new VerdictCheck.Recordable(
                run with
                {
                    Confidence = KeyConfidence.Medium,
                },
                $"crateside capped this verdict at MEDIUM: {HighNeeds}, and it lacked {missing}."
            );
    }

    internal static RunRecord WithNote(RunRecord run, string? note) =>
        note is null
            ? run
            : run with
            {
                Reasoning = string.Join("\n\n", new[] { run.Reasoning, note }.OfType<string>()),
            };

    /// <summary>The run, with what crateside knows and the session cannot.</summary>
    internal RunRecord Complete(
        RunRecord run,
        WorkOrder order,
        LedgerRun? held,
        JudgeDirection? direction,
        string? audio,
        IReadOnlyList<DetectedWindow> detected
    )
    {
        workingSet.TryReadEvidence(order.TrackKey, out var evidence);

        return run with
        {
            RecordedAt = time.GetUtcNow(),
            Song = held?.Song ?? run.Song,
            SourceUrl = order.SourceUrl,
            InputPath = audio,
            InputKind = audio is null ? null : "full_mix",
            Windows =
            [
                .. detected
                    .Where(window => window.Evidence is not null)
                    .Select(window => window.Window),
            ],
            Direction = direction?.Note,
            Sources = direction?.Ear is { } ear
                ?
                [
                    .. run.Sources.Where(source => source.Kind is not SourceKind.Ear),
                    EarSource(ear),
                ]
                : run.Sources,
            Evidence = Evidence(evidence, detected),
        };
    }

    /// <summary>What the user said they hear, as a source, with how much it counted on the day.</summary>
    private RunSource EarSource(TonalCenter ear) =>
        new()
        {
            Via = "the maintainer's ear",
            Kind = SourceKind.Ear,
            States = ear.Name,
            StatesKey = ear.Collapsed,
            StatesMode = ear.Mode,
            Note =
                _options.Ear is EarWeight.HumanSource
                    ? "counted as a human source"
                    : "weighed as a hint",
        };

    private static string? Evidence(string? given, IReadOnlyList<DetectedWindow> detected)
    {
        var whole = given is null ? null : LateReading.Unstamped(given);

        if (whole is null || detected is [] || JsonNode.Parse(whole) is not JsonObject root)
            return whole;

        root["windows"] = new JsonArray([
            .. detected
                .Where(window => window.Evidence is not null)
                .Select(window => new JsonObject
                {
                    ["start"] = window.Window.Start,
                    ["end"] = window.Window.End,
                    ["label"] = window.Window.Label,
                    ["evidence"] = JsonNode.Parse(window.Evidence!),
                }),
        ]);

        return root.ToJsonString();
    }
}
