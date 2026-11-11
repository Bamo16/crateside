using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;
using Humanizer;
using Microsoft.Extensions.Options;

namespace Crateside.Api;

public interface IRejudgeRequests
{
    RejudgeOutcome Request(TrackRow row, RejudgeRequest request);
}

/// <summary>Queues or starts a re-judge for the API, through the services the song page uses, and records who asked (#172).</summary>
public sealed class RejudgeRequests(
    LedgerStore ledgerStore,
    RejudgeQueue queue,
    IBarRuns barRuns,
    StemWarpReader warps,
    JobStore jobs,
    ActionLog actions,
    IOptions<JudgeOptions> judge,
    TimeProvider time
) : IRejudgeRequests
{
    private const string DefaultReason = "requested through the API";

    public RejudgeOutcome Request(TrackRow row, RejudgeRequest request)
    {
        if (request is not { RequestedBy: { } by } || string.IsNullOrWhiteSpace(by))
            return new RejudgeOutcome.Refused(
                StatusCodes.Status400BadRequest,
                "requestedBy is required: name who is asking, such as \"agent: coordination\""
            );

        by = by.Trim();

        var reason = request.Reason?.Trim() is { Length: > 0 } given ? given : DefaultReason;

        if (!judge.Value.IsConfigured)
            return new RejudgeOutcome.Refused(
                StatusCodes.Status409Conflict,
                "no judge is configured"
            );

        return request.Windows is { Count: > 0 } asked
            ? StartFromWindows(row, asked, by, reason)
            : Queue(row, by, reason);
    }

    private RejudgeOutcome Queue(TrackRow row, string by, string reason)
    {
        var ledger = ledgerStore.Read();

        if (ledger.SongFor(row.TrackKey) is not { } song)
            return new RejudgeOutcome.Refused(
                StatusCodes.Status409Conflict,
                "this track has no earlier judgment to redo; it waits for its first one"
            );

        var cutoff = time.GetUtcNow();

        queue.Add(song, cutoff, reason, by);
        actions.Record(row.TrackKey, UserActionKind.Rejudged, reason, by: by);

        return new RejudgeOutcome.Accepted(
            new RejudgeAccepted
            {
                Mode = RejudgeMode.Queued,
                VideoId = row.TrackKey,
                Song = song,
                RequestedBy = by,
                Reason = reason,
                Cutoff = cutoff,
                Position = PositionOf(song, ledger),
                WhyNot = StageEligibility.WhyNot(
                    jobs,
                    row.TrackKey,
                    JobStage.Verdict,
                    VerdictSelector.DefaultMaxStrikes,
                    retrySucceeded: true
                ),
                Windows = [],
            }
        );
    }

    private RejudgeOutcome StartFromWindows(
        TrackRow row,
        IReadOnlyList<RejudgeWindow> asked,
        string by,
        string reason
    )
    {
        if (!row.HasUpload)
            return new RejudgeOutcome.Refused(
                StatusCodes.Status409Conflict,
                "this track has no upload to judge from"
            );

        var group = warps.GroupOf(row);
        List<RunWindow> windows = [];

        foreach (var (window, index) in asked.Select((window, index) => (window, index + 1)))
        {
            if (Read(window, group, out var read) is { } problem)
                return new RejudgeOutcome.Refused(
                    StatusCodes.Status400BadRequest,
                    $"window {index}: {problem}"
                );

            windows.Add(read);
        }

        var direction = new JudgeDirection(windows, null, null, barRuns.AudioFor(row));

        if (!barRuns.TryStart(BarJudging.OrderFor(row), direction))
            return new RejudgeOutcome.Refused(
                StatusCodes.Status409Conflict,
                "a judgment from bars is already running for this track"
            );

        actions.Record(
            row.TrackKey,
            UserActionKind.Rejudged,
            $"{reason} ({"window".ToQuantity(windows.Count)})",
            by: by
        );

        return new RejudgeOutcome.Accepted(
            new RejudgeAccepted
            {
                Mode = RejudgeMode.Started,
                VideoId = row.TrackKey,
                Song = ledgerStore.Read().SongFor(row.TrackKey),
                RequestedBy = by,
                Reason = reason,
                Windows =
                [
                    .. windows.Select(window => new RejudgeWindowView
                    {
                        StartSeconds = window.Start,
                        EndSeconds = window.End,
                        Label = window.Label,
                        Bars = window.Bars?.Text,
                    }),
                ],
            }
        );
    }

    /// <summary>The problem with a window, or null with it read into <paramref name="read"/>.</summary>
    private static string? Read(RejudgeWindow window, WarpGroup group, out RunWindow read)
    {
        read = null!;

        var label = window.Label?.Trim() is { Length: > 0 } named ? named : null;

        switch (window)
        {
            case { Start.ValueKind: JsonValueKind.String, End.ValueKind: JsonValueKind.String }:
                if (group.Lender is null)
                    return "this track has no warp to read bars through; give seconds";

                var text = $"{window.Start.GetString()}–{window.End.GetString()}";

                if (JudgeRanges.Window(group, text, label) is not { } bars)
                    return $"\"{text}\" does not read as bars of this track";

                read = bars;

                return null;
            case { Start.ValueKind: JsonValueKind.Number, End.ValueKind: JsonValueKind.Number }:
                var (start, end) = (window.Start.GetDouble(), window.End.GetDouble());

                if (start < 0 || end <= start || (group.Seconds > 0 && start >= group.Seconds))
                    return "seconds must run forward from 0 and inside the track";

                read = new RunWindow(
                    start,
                    group.Seconds is 0 ? end : Math.Min(end, group.Seconds),
                    label,
                    WindowAsker.User
                );

                return null;
            default:
                return "start and end are both bars (strings) or both seconds (numbers)";
        }
    }

    private int? PositionOf(string song, KeyLedger ledger)
    {
        var pending = queue.Pending(ledger);

        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i].Song.Equals(song, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }

        return null;
    }
}

public abstract record RejudgeOutcome
{
    private RejudgeOutcome() { }

    public sealed record Accepted(RejudgeAccepted Result) : RejudgeOutcome;

    public sealed record Refused(int Status, string Why) : RejudgeOutcome;
}

public sealed record RejudgeRequest
{
    /// <summary>Who is asking, as free text: <c>agent: coordination</c>, <c>maintainer</c>.</summary>
    public string? RequestedBy { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<RejudgeWindow>? Windows { get; init; }
}

/// <summary>One stretch to judge: both ends bars in Live's positions (strings) or both seconds (numbers).</summary>
public sealed record RejudgeWindow
{
    public JsonElement Start { get; init; }

    public JsonElement End { get; init; }

    public string? Label { get; init; }
}

public sealed record RejudgeAccepted
{
    public required RejudgeMode Mode { get; init; }

    public required string VideoId { get; init; }

    public string? Song { get; init; }

    public required string RequestedBy { get; init; }

    public required string Reason { get; init; }

    /// <summary>Queued: a run from this moment on settles the entry.</summary>
    public DateTimeOffset? Cutoff { get; init; }

    /// <summary>Queued: its place among the songs owed a re-judge, from 1.</summary>
    public int? Position { get; init; }

    /// <summary>Queued: what stops the verdict stage taking it up as things stand, as in <c>/api/stages</c>.</summary>
    public SkipCause? WhyNot { get; init; }

    /// <summary>Started: the windows the run was given, in seconds.</summary>
    public required IReadOnlyList<RejudgeWindowView> Windows { get; init; }
}

public sealed record RejudgeWindowView
{
    public required double StartSeconds { get; init; }

    public required double EndSeconds { get; init; }

    public string? Label { get; init; }

    public string? Bars { get; init; }
}

public enum RejudgeMode
{
    Queued,
    Started,
}
