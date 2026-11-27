namespace Crateside.Core.Tracks;

/// <summary>Why the judge's endpoint last refused a session for a track, and when judging tries again (ADR 0070).</summary>
public sealed record JudgeRefusal(string Reason, DateTimeOffset? RetryAt)
{
    /// <summary>The whole sentence a row or page says: the reason, then when judging tries again or that it is paused.</summary>
    public string Says(JudgeStanding judge) => $"judge refused: {Reason} · {Next(judge)}";

    /// <summary>For a chip that has no room for the reason.</summary>
    public string Short(JudgeStanding judge) => $"refused · {Next(judge)}";

    private string Next(JudgeStanding judge) =>
        (judge, RetryAt) switch
        {
            (JudgeStanding.Paused, _) => "judging paused",
            (_, { } at) => $"next try {at.ToLocalTime():t}",
            _ => "next try shortly",
        };
}
