using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Core.Tracks;

/// <summary>Everything crateside knows about one track, gathered from the stores the pages read (#167).</summary>
public sealed record TrackDetails
{
    [JsonPropertyName("videoId")]
    public required string TrackKey { get; init; }

    /// <summary>The track's row, as the Tracks page and Track details draw it; null when crateside has no row for it.</summary>
    public TrackRow? Row { get; init; }

    /// <summary>The evidence behind the latest verdict: its sources, sections and every detector's readings.</summary>
    public RunEvidence? Evidence { get; init; }

    /// <summary>Every line <c>jobs.jsonl</c> holds for any of the track's uploads, oldest first.</summary>
    public IReadOnlyList<JobUpdate> Jobs { get; init; } = [];

    /// <summary>What the warp pass and loudness did to the track's samples, one line each, oldest first.</summary>
    public IReadOnlyList<StageNote> StageNotes { get; init; } = [];

    /// <summary>Each sample on disk, with its tags, its parsed warp and crateside's records about that warp.</summary>
    public IReadOnlyList<StemDetails> Stems { get; init; } = [];

    /// <summary>The track's entry in each per-song store, under every key it is filed under.</summary>
    public required FiledRecords Records { get; init; }
}

/// <summary>What Track details draws: the track's row and the evidence behind its verdict.</summary>
public sealed record TrackPage(TrackRow? Row, RunEvidence? Evidence);

/// <summary>Reads one track for Track details and for the API, so the two cannot disagree.</summary>
public sealed class TrackDetailsReader(
    TracksService tracks,
    LedgerStore ledger,
    JobStore jobs,
    StageNotes stageNotes,
    StemDetailsReader stems,
    FiledRecordsReader records,
    PageChoiceStore choices
)
{
    /// <summary>The row from the rows as last built, and the evidence of its song, else of the video ID's.</summary>
    public TrackPage Find(string trackKey)
    {
        var row = tracks.Current?.Rows.FirstOrDefault(row =>
            row.TrackKey.Equals(trackKey, StringComparison.Ordinal)
        );

        return new(
            row,
            (
                row is { Verdict.Song: var song }
                    ? ledger.EvidenceFor(song)
                    : ledger.EvidenceForTrackKey(trackKey)
            )
                is { } evidence
                ? choices.Marked(trackKey, evidence)
                : null
        );
    }

    /// <summary>Everything about the track, or null when crateside has neither a row nor a verdict for it.</summary>
    public TrackDetails? Read(string trackKey)
    {
        var (row, evidence) = Find(trackKey);

        if (row is null && evidence is null)
            return null;

        var uploads = new HashSet<string>(
            [trackKey, .. row?.Facts.Uploads ?? []],
            StringComparer.Ordinal
        );

        return new()
        {
            TrackKey = trackKey,
            Row = row,
            Evidence = evidence,
            Jobs = [.. jobs.Updates().Where(update => uploads.Contains(update.TrackKey))],
            StageNotes = stageNotes.For(uploads),
            Stems = [.. (row?.Samples ?? []).Select(stems.Read)],
            Records = records.Read([
                .. uploads,
                .. new[] { row?.Song, evidence?.Song }.OfType<string>(),
            ]),
        };
    }
}
