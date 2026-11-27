# 0073 — Warp and loudness say what they did in a feed channel of their own

- Status: Accepted
- Date: 2026-11-01
- Extends #170 (the activity feed's per-stage summaries), which gave download, evidence,
  separation, handoff and tagging, and verdict a `summary` on their job updates.

## Context

The warp pass and loudness have no job record: they run on a timer and on file changes, over every
sample in Staging and the library, not as a stage of one track.

## Decision

- **A separate channel, not new `JobStage` values.** `StageProgress` and `StageFlow` describe the
  pipeline a track moves through; neither pass is a step of it, and a stage value would make every
  track read as waiting on it.
- **`stage-notes.jsonl`** in `%APPDATA%\crateside`, one line per note: when, the upload's video ID,
  the pass (`Warp` or `Loudness`) and a summary of about 80 characters. It is append-only like
  `actions.jsonl`, read through `StageNotes`.
- **Written by the passes from the result they hold.** `WarpLending` records one for each stem it
  wrote, from the `WarpOutcome` (a starter's tempo and marker, why a clip got Warp off, whose warp
  was borrowed). `Leveling` records one for each sidecar whose gain it set, from the gain and the
  target. The upload comes from the stem's `source` tag, which is the one read of a file each makes
  to find the song; the summary is built from the result.
- **Filed under the upload**, as the job lines are. The feed and the API read the notes of every
  upload of the song.
- **A summary leads with the stem's preset** ("Vocal - Full: …") because a song has several stems
  and each is written on its own.
- **The feed** merges the notes with the job lines in time order, under the pass's name as the
  source. A song with no notes reads as before. The API returns them as `stageNotes` beside `jobs`.
- Only successes write a note. A lending that was refused or failed keeps to the log.
