# 0110 - A Warp-off clip says why, and a later stage clears an earlier parking

- Status: Accepted
- Date: 2026-11-20
- Extends [ADR 0059](0059-a-sample-arrives-at-one-loudness.md) (a stem with no starter gets a clip
  with Warp off). The rules' tiers of
  [ADR 0076](0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md) are unchanged.

## Context

A read of the 21 tracks added on 2026-11-20 found four things that looked like bugs. One was not:
three rulings (Still Fly, Crank That, Bossy) went past a detector at 0.81 to 0.87 that named
another key. ADR 0076 allows that by its wording, since tier A needs only two of three detectors
and tier B only keydetect and one backer at r of 0.80 or more; the dissenter's own confidence is
never read. That stays a question for the maintainer, not a change made here.

## Decision

- **A clip with Warp off records why.** `written-warps.json` carries `warpOffBecause` beside
  `isUnwarped`, from every gate that sends a stem to Warp off: no stem to read a tempo from, no
  tempo reading yet, a reading whose 30 s windows drift (with the spread and the limit), and the
  checks of one tempo holding (too few clear transients, a step, a miss in ms). The API returns it
  under the stem's `warp.written`, and the stem's warp card on the song page says it. A clip
  written before this has none and keeps the old generic wording. No gate reads beatdetect's
  confidence, so a low confidence is never the reason by itself.
- **A stage given up on is cleared by a later stage that succeeded after it** (`StageProgress`).
  A track whose verdict was carried over from another upload no longer reads "gave up" over a
  Verdict job that failed before the carry.
- **A poor-source shortfall parks a track only until it is cut.** Once a track has a verdict and
  its stems exist, the shortfall no longer reads as its parking.
- **A source can be added, or a file adopted, from the moment a track has a verdict.** Neither
  needs separation: the add restates the verdict onto the new upload and puts it on the plan, and
  the adoption takes a file in where it is. The openers no longer wait for the plan to be open.
