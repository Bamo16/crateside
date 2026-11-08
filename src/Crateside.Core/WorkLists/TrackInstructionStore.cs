using Crateside.Core.Ledger;
using Crateside.Core.Plans;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.WorkLists;

/// <summary>
/// The user's standing instructions about particular tracks, kept in the ledger database
/// (ADR 0083). Each keeps an in-memory copy, loaded on first use.
/// </summary>
public sealed class TrackInstructionStore(LedgerStore ledger)
{
    private readonly Lock _gate = new();

    private Dictionary<string, TrackInstruction>? _cached;

    public IReadOnlyList<TrackInstruction> Read()
    {
        lock (_gate)
            return [.. (_cached ??= Load()).Values];
    }

    /// <summary>
    /// Records which files the user wants made for a track. Null clears the choice, putting the
    /// track back on <see cref="StemPlan.Default"/>.
    /// </summary>
    public void SetPlan(string trackKey, StemPlan? plan) =>
        Upsert(trackKey, track => track with { Plan = plan });

    /// <summary>
    /// Records what the user calls a track. Null or blank clears it, restoring the derived name.
    /// </summary>
    public void SetName(string trackKey, string? name) =>
        Upsert(
            trackKey,
            track => track with { Name = name?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>Records that a track is not the ledger song its name looks like (#41).</summary>
    public void SetDifferentFrom(string trackKey, string song) =>
        Upsert(trackKey, track => track with { DifferentFrom = song });

    /// <summary>Records that the user asked for this track's files to be made again (#50).</summary>
    public void RequestCut(string trackKey, DateTimeOffset at) =>
        Upsert(trackKey, track => track with { CutRequestedAt = at });

    /// <summary>Records that the user takes this upload's audio as it comes (#31).</summary>
    public void AcceptUpload(string trackKey, string upload) =>
        Upsert(trackKey, track => track with { AcceptedUpload = upload });

    /// <summary>Records that crateside should work a track the playlist does not hold.</summary>
    public void AddByUrl(string trackKey) =>
        Upsert(trackKey, track => track with { AddedByUrl = true });

    /// <summary>Leaves the track's verdict to the weekly spend-down rather than the judging stage (ADR 0063).</summary>
    public void MarkBackfill(string trackKey) =>
        Upsert(trackKey, track => track with { Backfill = true });

    /// <summary>Drops crateside's own record of a track, which is what removing one means.</summary>
    public void Forget(string trackKey) =>
        Upsert(trackKey, _ => new TrackInstruction { TrackKey = trackKey });

    /// <summary>
    /// Records the upload to fetch this track from instead of its playlist entry's own. Null or
    /// blank clears it, restoring the entry's video.
    /// </summary>
    public void SetSourceUrl(string trackKey, string? url) =>
        Upsert(
            trackKey,
            track => track with { SourceUrl = url?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>
    /// Applies a change to one track's instruction, adding it if there was none and dropping it
    /// again once it says nothing.
    /// </summary>
    private void Upsert(string trackKey, Func<TrackInstruction, TrackInstruction> change)
    {
        lock (_gate)
        {
            var instructions = _cached ??= Load();
            var changed = change(
                instructions.GetValueOrDefault(trackKey)
                    ?? new TrackInstruction { TrackKey = trackKey }
            );

            ledger.Save(context =>
            {
                context.TrackInstructions.RemoveRange(
                    context.TrackInstructions.Where(row => row.TrackKey == trackKey)
                );

                if (changed.SaysSomething)
                    context.TrackInstructions.Add(TrackInstructionRow.From(changed));
            });

            if (changed.SaysSomething)
                instructions[trackKey] = changed;
            else
                instructions.Remove(trackKey);
        }
    }

    private Dictionary<string, TrackInstruction> Load() =>
        ledger
            .Query(context => context.TrackInstructions.AsNoTracking().ToList())
            .ToDictionary(row => row.TrackKey, row => row.ToInstruction(), StringComparer.Ordinal);
}
