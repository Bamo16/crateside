using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Tracks;

namespace Crateside.Api;

public interface ITrackMarkRequests
{
    MarkOutcome Change(TrackRow row, string mark, bool marked, MarkRequest request);
}

/// <summary>Sets or clears a track's mark for the API, through the store the song page uses, and records who asked.</summary>
public sealed class TrackMarkRequests(TrackMarkStore marks, ActionLog actions) : ITrackMarkRequests
{
    public MarkOutcome Change(TrackRow row, string mark, bool marked, MarkRequest request)
    {
        if (request is not { RequestedBy: { } by } || string.IsNullOrWhiteSpace(by))
            return new MarkOutcome.Refused(
                StatusCodes.Status400BadRequest,
                "requestedBy is required: name who is asking, such as \"agent: coordination\""
            );

        if (
            !Enum.TryParse<TrackMark>(mark, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed)
        )
            return new MarkOutcome.Refused(
                StatusCodes.Status400BadRequest,
                $"unknown mark \"{mark}\"; the marks are {string.Join(", ", Enum.GetNames<TrackMark>())}"
            );

        if (marks.Of(row.Song).Contains(parsed) != marked)
        {
            marks.Set(row.Song, parsed, marked);
            actions.Record(
                row.TrackKey,
                marked ? UserActionKind.MarkSet : UserActionKind.MarkCleared,
                parsed.Label(),
                by: by.Trim()
            );
        }

        return new MarkOutcome.Changed(
            new MarkChanged
            {
                VideoId = row.TrackKey,
                Song = row.Song,
                Marks = marks.Of(row.Song),
            }
        );
    }
}

public abstract record MarkOutcome
{
    private MarkOutcome() { }

    public sealed record Changed(MarkChanged Result) : MarkOutcome;

    public sealed record Refused(int Status, string Why) : MarkOutcome;
}

public sealed record MarkRequest
{
    /// <summary>Who is asking, as free text: <c>agent: coordination</c>, <c>maintainer</c>.</summary>
    public string? RequestedBy { get; init; }
}

/// <summary>The track's marks once the request has been applied.</summary>
public sealed record MarkChanged
{
    public required string VideoId { get; init; }

    public required string Song { get; init; }

    public required IReadOnlyList<TrackMark> Marks { get; init; }
}
