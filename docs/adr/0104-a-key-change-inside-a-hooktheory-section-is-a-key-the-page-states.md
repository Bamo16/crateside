# 0104 - A key change inside a Hooktheory section is a key the page states

- Status: Accepted
- Date: 2026-11-17
- Follows [ADR 0076](0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md) (rules
  decide what they can measure) and [ADR 0095](0095-a-second-key-centre-counts-only-when-something-independent-of-the-judge-places-it.md)
  (a second key centre needs independent support). Relates to
  [#149](https://github.com/Bamo16/crateside/issues/149) (verdicts reached without seeing a
  TheoryTab's in-section key change).

## Context

A TheoryTab section's score can change key partway through ("Outro: F# dorian → F# mixolydian").
The page showed that, and the judge's work order lists it, but the rules read only each section's
first key. A page that modulates inside a section could therefore back a ruling on the first key
while stating another.

## Decision

**Every key a section's score moves to after its first counts as a key the page states**, the same
claim as a section's own key. `VerdictRules` adds them beside the page's section keys, so Tier A
(every stated key is the detectors' key) fails when a section modulates to another centre.

Keys compare as before: enharmonic spellings are equal and a mode collapses to its major or minor,
so a shift to a mode on the same tonic and colour (B minor → B dorian) still agrees, while F#
dorian → F# mixolydian (minor → major) does not.

The ruling's source statement is unchanged: it still reads from the section keys and the page's own
"Outro: A → B" line, not from the added keys.

## Consequences

- Rulings stay where they were unless a counted page's section changes centre inside itself; then
  the track is left open for the judge or the person.
- Nothing is re-ruled in bulk. Rules run for tracks without a verdict, and again on a page choice
  for a track the rules still hold; a judged or person-decided track is never touched. Measured
  read-only on 2026-11-17 against the 32 tracks the ledger holds as ruled by crateside, with the
  pages kept for each: none states a key change inside a section, so no ruling would change.
- #149's six tracks were judged, not ruled, so this changes nothing for them.
