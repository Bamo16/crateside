namespace Crateside.Core.Tracks;

/// <summary>Whether a judge will reach the tracks the rules left open; the person may decide them in every case (ADR 0076).</summary>
public enum JudgeStanding
{
    /// <summary>A judge is configured and judging is on.</summary>
    Running,

    /// <summary>A judge is configured and the user has paused judging.</summary>
    Paused,

    /// <summary>No judge is configured.</summary>
    Absent,
}
