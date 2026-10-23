using Crateside.Core.Plans;

namespace Crateside.Core.WorkLists;

/// <summary>The user's standing instructions about particular tracks, kept in <see cref="QueueFile"/>.</summary>
public sealed class TrackInstructionStore(QueueFile file)
{
    public IReadOnlyList<TrackInstruction> Read() => file.Read().Tracks;

    /// <summary>
    /// Records which files the user wants made for a track. Null clears the choice, putting the
    /// track back on <see cref="StemPlan.Default"/>.
    /// </summary>
    public void SetPlan(string videoId, StemPlan? plan) =>
        Upsert(videoId, track => track with { Plan = plan });

    /// <summary>
    /// Records what the user calls a track. Null or blank clears it, restoring the derived name.
    /// </summary>
    public void SetName(string videoId, string? name) =>
        Upsert(
            videoId,
            track => track with { Name = name?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>Records that a track is not the ledger song its name looks like (#41).</summary>
    public void SetDifferentFrom(string videoId, string song) =>
        Upsert(videoId, track => track with { DifferentFrom = song });

    /// <summary>Records that the user asked for this track's files to be made again (#50).</summary>
    public void RequestCut(string videoId, DateTimeOffset at) =>
        Upsert(videoId, track => track with { CutRequestedAt = at });

    /// <summary>Records that the user takes this upload's audio as it comes (#31).</summary>
    public void AcceptUpload(string videoId, string upload) =>
        Upsert(videoId, track => track with { AcceptedUpload = upload });

    /// <summary>Records that crateside should work a track the playlist does not hold.</summary>
    public void AddByUrl(string videoId) =>
        Upsert(videoId, track => track with { AddedByUrl = true });

    /// <summary>Leaves the track's verdict to the weekly spend-down rather than the judging stage (ADR 0063).</summary>
    public void MarkBackfill(string videoId) =>
        Upsert(videoId, track => track with { Backfill = true });

    /// <summary>Drops crateside's own record of a track, which is what removing one means.</summary>
    public void Forget(string videoId) =>
        Upsert(videoId, _ => new TrackInstruction { VideoId = videoId });

    /// <summary>
    /// Records the upload to fetch this track from instead of its playlist entry's own. Null or
    /// blank clears it, restoring the entry's video.
    /// </summary>
    public void SetSourceUrl(string videoId, string? url) =>
        Upsert(
            videoId,
            track => track with { SourceUrl = url?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>
    /// Applies a change to one track's instruction, adding it if there was none and dropping it
    /// again once it says nothing.
    /// </summary>
    private void Upsert(string videoId, Func<TrackInstruction, TrackInstruction> change) =>
        file.Save(document =>
        {
            var existing =
                document.Tracks.FirstOrDefault(track =>
                    track.VideoId.Equals(videoId, StringComparison.Ordinal)
                ) ?? new TrackInstruction { VideoId = videoId };

            var changed = change(existing);

            return document with
            {
                Tracks =
                [
                    .. document.Tracks.Where(track =>
                        !track.VideoId.Equals(videoId, StringComparison.Ordinal)
                    ),
                    .. changed.SaysSomething ? new[] { changed } : [],
                ],
            };
        });
}
