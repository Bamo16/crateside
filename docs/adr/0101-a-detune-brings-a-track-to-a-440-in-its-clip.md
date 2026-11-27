# 0101 - A detune brings a track to A = 440 in its clip

- Status: Accepted
- Date: 2026-11-17
- Implements [#187](https://github.com/Bamo16/crateside/issues/187) (detune to A = 440); design brief
  `docs/design/briefs/24-detune-to-a440.md`.

## Context

Some recordings sit a fraction of a semitone off A = 440. Ableton keeps a clip's **Detune** in the
analysis file (`PitchFine`, cents), so a correction there brings the sample to concert pitch beside
the rest of a set. Ableton changes pitch only in re-pitch warp mode, so the detune needs no gating
by warp mode (Bamo, 2026-11-17).

## Decision

- **Reading.** keydetect prints `tuning_cents` per run. `TuningPass` reads up to five 30 s stretches,
  spaced evenly across the instrumental stem (the source when there is none).
- **Steady** is 3 or more stretches within 6 cents. Fewer readings leave the track undecided.
- **Write** the negated median when it is 10 to 30 cents off. Under 10 is in tune; above 30 (near a
  quarter tone) or unsteady with a reading of 10 or more goes to the **review list**, unwritten.
- **Detune** is written into every saved default clip of the track, clamped to ±50. A clip that
  holds a detune crateside did not write is left alone.
- **Put back** restores 0 and the track is not written again. **Accept** writes a review track's
  negated median.
- **Backfill.** `TuningWorker` runs the pass on app start, on file changes and every ten minutes, so
  existing tracks are covered without an action. Decisions live in `tuning.json`; a note under
  stage *Detune* records the first write.

## Consequences

Nothing re-reads a decided track, so a detector change does not move written detunes.
