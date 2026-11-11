using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>What a selector decided about one track: in line to be picked, or passed over and why.</summary>
public abstract record Selection
{
    private Selection() { }

    public sealed record Picked : Selection;

    public sealed record Skipped(SkipCause Cause) : Selection;

    public static Selection From(SkipCause? cause) =>
        cause is { } skipped ? new Skipped(skipped) : new Picked();
}

/// <summary>Why a selector passes a waiting track over.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SkipCause>))]
public enum SkipCause
{
    /// <summary>No source crateside can use (private, removed, or region-locked).</summary>
    Unavailable,

    /// <summary>The stage already succeeded, and this stage never repeats a success unasked.</summary>
    AlreadyDone,

    /// <summary>A job for this stage is queued or running for the track.</summary>
    InFlight,

    /// <summary>The stage is waiting on the person.</summary>
    AwaitingPerson,

    /// <summary>Out of strikes: failed too often to be tried again.</summary>
    GaveUp,

    /// <summary>Failed in a way no retry can fix.</summary>
    FailedPermanently,

    /// <summary>A failed download waiting out its backoff.</summary>
    WaitingOutBackoff,

    /// <summary>The audio it needs has not been downloaded.</summary>
    NoAudioYet,

    /// <summary>The audio is held already, so there is nothing to fetch.</summary>
    AlreadyHasAudio,

    /// <summary>No video to download this track from.</summary>
    NotFetchable,

    /// <summary>The audio was swept up after a successful separation.</summary>
    SweptUp,

    /// <summary>Detection already has its evidence.</summary>
    AlreadyHasEvidence,

    /// <summary>The download is still in progress.</summary>
    Downloading,

    /// <summary>Not detected yet, so there is nothing to judge from.</summary>
    NoEvidence,

    /// <summary>The ledger already has a verdict for the track.</summary>
    AlreadyJudged,

    /// <summary>No judge is configured, so judging that waits on one cannot run.</summary>
    NoJudgeConfigured,

    /// <summary>The source is poor enough that a better upload is wanted first.</summary>
    PoorSourceAwaitingSwap,

    /// <summary>The name looks like a song already judged.</summary>
    LooksLikeJudgedSong,

    /// <summary>A library backfill track, judged only in the weekly spend-down.</summary>
    BackfillWaitsForSpendDown,

    /// <summary>Separation has not settled, or failed, so its stems are not final.</summary>
    SeparationNotSettled,
}

/// <summary>One waiting track and what its stage's selector decided about it.</summary>
public sealed record Decision(string TrackKey, string Title, Selection Selection);
