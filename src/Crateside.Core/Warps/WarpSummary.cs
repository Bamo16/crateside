using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>The one line the activity feed shows for a stem the warp pass wrote (#170).</summary>
public static class WarpSummary
{
    /// <summary>Null for a loan that wrote nothing.</summary>
    public static string? Of(WarpLoan loan) =>
        What(loan.Outcome) is { } what
            ? StemFileName.PresetOf(loan.Target) is { } preset
                ? $"{preset}: {what}"
                : what
            : null;

    private static string? What(WarpOutcome outcome) =>
        outcome switch
        {
            WarpOutcome.Written(var from, _) =>
                $"borrowed the warp of {StemFileName.PresetOf(from) ?? Path.GetFileName(from)}",
            WarpOutcome.Started(var bpm, _, var markerSeconds) =>
                $"starter warp at {bpm:0.00} BPM, marker at {markerSeconds:0.00} s",
            WarpOutcome.Unwarped(_, var because) => $"Warp off: {because ?? "no tempo reading"}",
            _ => null,
        };
}
