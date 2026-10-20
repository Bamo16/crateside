# 0060 — A starter is written only where one tempo holds on the beats

- Status: Accepted
- Date: 2026-10-19
- Amends [ADR 0048](0048-stems-arrive-in-staging-already-warped.md): a steady beatdetect reading
  is no longer enough for a starter warp. Builds on
  [ADR 0059](0059-a-sample-arrives-at-one-loudness.md)'s clip with Warp off.

## Context

A starter is written at beatdetect's tempo when the interquartile range of its 30 s windows is
under 0.1% (ADR 0048). That gate misses a tempo that wanders and comes back (the middle half of the
windows still agree), a step (the windows either side agree), and a track beatdetect itself reports
as moving, which the gate never looked at. MEASURED 2026-10-19 on the six starters crateside had
written in staging, laying each starter's grid against the instrumental's strong transients: two
held (*Hey Ya!* within 1 ms over every 30 s, GYARI 4 ms) and four did not (*drivers license* 34 ms,
*Nineteen* 27 ms with a step, *Kiss from a Rose* 79 ms with five, *Action in Memphis* 83 ms, flagged
as moving). The maintainer had noticed the last two in Live: they were labelled *Check beat*, as a
good starter is, while only averaging a moving tempo.

The maintainer's rule: a stem is auto-warped only where it can be done properly, a steady tempo
on a grid, not necessarily on the downbeat. Otherwise it arrives unwarped, marked as such.

## Decision

**A starter is written only when one tempo keeps the group's beats on its grid through the whole
track.** The starter's tempo (the reading, refined on the transients as before) is laid against the
reference stem's stronger transients, phased to fit them, and must hold: at least six 10 s windows,
no step, and every 30 s within **10 ms** of every other. A reading beatdetect reports as moving gets
no starter either.

**A stem that fails gets ADR 0059's clip with Warp off** (its gain and colour, no warp) and the
*Warp by hand* Collection. That includes a starter written before this check: it is replaced the
next time crateside looks at it. A starter and a clip with Warp off now replace each other as the
tempo allows; a lent warp or the user's own is never replaced.

**The threshold, MEASURED 2026-10-19** over the 450 library tracks whose reading would give a
starter, against the user's own warp of each:

| the user's warp | tracks | pass at 10 ms |
|---|---|---|
| steady | 394 | 345 (88%) |
| changes tempo | 34 | 11 |
| has a step | 22 | 0 |

The steady warps' spread has a median of 4.1 ms and a 90th percentile of 11.7 ms. 8 ms keeps 314,
15 ms keeps 370 but lets one stepped track through. Correcting the starter's tempo for its measured
drift before checking (as a true-up does) rescued only 4 more, so it was left out. The tracks that
change tempo and still pass are ones a single tempo serves within 10 ms anyway.

## Consequences

- **About one steady track in eight arrives unwarped** that used to get a starter. On those a
  single tempo drifts 10 to 30 ms across the track, which is the starter the maintainer did not
  want.
- The check uses the transients the starter would carry: Live's where the stem has a Live analysis,
  else crateside's own detector's. The measurement above used Live's.
- A stem whose reading is not in yet still gets the clip with Warp off first, and a starter once
  the reading arrives and holds.
