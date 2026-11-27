# 0100 - A key lift places a second key centre

- Status: Accepted
- Date: 2026-11-17
- Implements [#171](https://github.com/Bamo16/crateside/issues/171) (a second key centre needs
  independent support before it counts as a key change). Supersedes the *Detectors* leg of
  [ADR 0095](0095-a-second-key-centre-counts-only-when-something-independent-of-the-judge-places-it.md);
  its other legs and record-time checks stand.

## Context

ADR 0095 confirmed a section when two of keydetect, madmom and Essentia read its centre in a window
inside it and the primary in a window outside. That rule was never measured, and it needs windows,
which most verdicts don't have. keydetect-harness report 15 (2026-11-16) measured two detectors on
every library verdict with a second centre:

- **The transposed-repeat matcher** finds earlier music coming back shifted. On its own it fires on
  ordinary repeats: on *Persona* it finds copies at +2 and +11 that are no key change.
- **The two-key fit** scores, at each point, how much better a key and its transposition fit either
  side than one key fits the whole. On its own it peaks wherever the harmony moves.
- **Where both agree**, on the same step within 10 s, only real changes were left: TAPI・TAPI's
  final section, E to F at 122.9 s (lifts at 116 and 124 s, fit 0.279), and in an unlabelled track
  *Invisible Touch* +2 at 160 s.

The maintainer decided the rule on 2026-11-16: the two agreeing counts as support; the matcher
alone doesn't; finding nothing never counts against a section.

## Decision

- **A key lift** is a transposed repeat the two-key fit agrees with: report 12a's matcher (1 s
  units of keydetect's chroma, a seed at 0.65 beating no shift by 0.15, runs of two, stretched while
  a 4-unit mean holds 0.5) and report 14's fit (0.5 s units, ±32 s windows, top four peaks 8 s
  apart), agreeing when a peak with the run's step and a score of at least 0.081 lies within 10 s of
  where the copy starts. `KeyLifts` ports both from the harness; the tests hold TAPI・TAPI and
  *Persona* to the harness's numbers.
- **A section is confirmed by a lift (`SectionSupport.KeyLift`)** when one begins between 8 s before
  the section's start and the next section's start, by the step into the section's centre from
  another centre in the verdict (the primary or another section), counted between tonics or between
  the major scales whose notes the two modes use. A section with no `start` can't be confirmed this
  way.
- **The Detectors leg is gone.** Two detectors in a window there no longer confirm a section; it
  was never tested and the new rule is.
- **Lifts are read over the whole track only**, by a second ffmpeg pass with `keydetect=chroma=1`
  (about 3 s a track), and stored in the run's evidence as `key_lifts`: absent where the track was
  never scanned, empty where nothing was found. A new evidence run reads them; the detector backfill
  adds them to existing runs over the source on disk without re-judging. Missing lifts never hold up
  judging.
- The judge prompt is told the new rule. It does not read the lifts; crateside applies them when the
  verdict is read, as ADR 0095 does for the other legs.

## Consequences

- **Measured 2026-11-17** on a copy of the ledger, lifts computed over each track's source: of 17
  latest verdicts with a section outside their primary, two change.
  - TEMPURA KIDZ, Moe Shop – *TAPI・TAPI*: the final section in F becomes confirmed once the
    backfill stores its lifts, and gets the Key caveat Collection on its next staging pass.
  - Snail's House – *Sunday*: its C# minor intro loses the Detectors confirmation (no lift) and is
    shown as the judge's hypothesis; it loses the Key caveat unless something else sets it.
  - *Persona* stays confirmed by the maintainer's window. BTS – *Dynamite* has a +2 lift at 161 s
    that matches its F# outro, but the outro has no `start`, so it stays a hypothesis.
    Queen – *Bohemian Rhapsody*, Roxette – *It Must Have Been Love* and White Shadow –
    *If You Like It* have no source on disk, so nothing can be scanned for them.
- The thresholds are the harness's, measured on the library's few real changes. A missed change
  costs only the caveat, since absence doesn't count against a section.
