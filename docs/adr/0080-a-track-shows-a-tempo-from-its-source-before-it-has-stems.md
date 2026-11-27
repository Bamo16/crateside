# 0080 - a track shows a tempo from its source before it has stems

- Status: Accepted
- Date: 2026-11-07
- Extends [ADR 0065](0065-the-starter-warps-beats-are-read-on-the-source.md) (beatdetect reads the
  source) and [ADR 0049](0049-a-bpm-shows-only-the-precision-crateside-can-vouch-for.md) (what a
  BPM shows). The tile was empty ("not measured") for a track waiting for its key, because the beat
  worker read only the sources of uploads that already had an instrumental in Staging or the library.

## Decision

**The beat worker also reads the source of every upload that has one on disk**, whether or not it has
stems, with the pinned build, the way it reads any file (whole file, then each 30 s). This is CPU
work that contends for nothing, so it runs unattended, and it is its own backfill: the first pass
after a restart reads every waiting track's source, whatever state its evidence is in, and never
touches the evidence or the judge.

**The reading is the ADR 0065 reading, made earlier.** It is the same call on the same file, so it
is kept in `beats.json` under the source's path, length and write time, and it is not read again once
the track is separated: the stems' pass finds the source already read. It is not stored in the
evidence JSON, which is the key detectors' record and moves into the ledger with the run; a second copy
of a reading `beats.json` already keys by file would drift from it.

**It is marked while the track has no stems.** The row's `Bpm` shows it as `~139` with the note
"from the full mix · until the stems are cut", never vouched, and ahead of the ledger's estimate. From
the first stem the headline is the reading it always was ("crateside's reading"), and a warp
replaces that as before.

**It feeds display only.** `TrackRow.FullMixBeats` is separate from `Beats`, which the Tempo-drifts
filter reads, and no starter warp, sidecar or off-its-tempo check is made for a track without stems.
Once stems exist the source reading is what ADR 0065 already has the starter warp use.

## Consequences

- A track waiting for its key shows a tempo; the waiting tracks' sources are read once.
- The API's tempo gains `isFromFullMix`, and a track row `fullMixBeats`.
