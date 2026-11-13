# 0095 - A second key centre counts only when something independent of the judge places it

- Status: Accepted
- Date: 2026-11-12
- Implements [#171](https://github.com/Bamo16/crateside/issues/171) (a second key centre needs
  independent support before it counts as a key change). Amends
  [ADR 0069](0069-a-verdict-is-checked-against-its-evidence-before-it-is-recorded.md): two more
  checks at record time. Follows [ADR 0076](0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md):
  crateside's rules decide what they can measure.

## Context

A verdict can name sections in different centres, and a verdict with sections in more than one
centre got the *Key caveat* Collection. Nothing required a source to back the second centre, so
the judge's own hypothesis became a recorded fact and marked the track in Live. keydetect's key
lines are running totals over the stream, so a key event is where the whole-song estimate flips,
not where a modulation happens; a section start read from them is late or arbitrary.

## Decision

**A section in a centre other than the verdict's primary is classified when the verdict is read,
from what the run stored**, as one of:

- **Human source**: a source of kind authoritative or corroborating states that section's key and
  names the section in its `sections`.
- **Asked**: the maintainer gave a window over it (a window labelled with the section's name, or
  lying between its start and the next section's start, asked by the user). The maintainer's own
  "Changes key" mark is unchanged and still sets the caveat by itself.
- **Detectors**: in a window inside the section two of keydetect, madmom and Essentia read that
  centre, and in a window outside it two of them read the primary. Measured on windows crateside
  ran, never taken from the judge's summary.
- **Hypothesis**: none of those.

"Centre" is compared as a key (`MusicKey`): enharmonic spellings are equal, and a mode collapses to
its major or minor, so B Dorian beside B minor is not a second centre.

A hypothesis is shown as "possible key change (the judge's reading, unconfirmed)" with the start
it proposed and a "Judge these bars" button that opens the bar form filled with that stretch. It
does not get the Key caveat, which now comes from a confirmed section, a modal, LOW or withheld
verdict, or the maintainer's mark.

**The classification is derived, not a column.** It depends on the run's windows, sources and
evidence JSON, which are edited after the fact (late readings, a removed source), and deriving it
at read time reaches every existing verdict on the next normal read without a migration or a
backfill. `Section.Support` carries it; the ledger loads evidence only for runs that have a
section outside their primary.

**Record-time checks** (`RecordTimeCheck`, beside ADR 0069's):

- **Sections are no exit from withholding.** A MEDIUM or HIGH verdict whose sections are all
  unconfirmed, with a primary that no two detectors back (whole track or in a window) and no human
  source states, is capped at LOW, once, with a note. A confirmed section lifts it.
- **When windows were judged, the primary is one a window read or a human source states.** A
  primary that only an unbacked whole-file read holds (fewer than two detectors) makes the file
  unusable, and the session is resumed once as for any refused file. A window "read" a centre when
  any detector named it there: one detector is weak, which is why the whole-file path needs two.

**The prompt** is told the rule, no longer presents key events as change points (a `start` is left
out unless a window or source places it), and the work order lists how the keys in play relate
(relative, parallel, notes shared), computed by `KeyRelations` from each key's seven scale notes,
so the judge does not work it out from the names. The 2026-10-31 case it came from (G# minor
called "the same notes respelled" as A# minor) shares five notes, not seven.

## Consequences

- **Measured 2026-11-12** on a copy of the ledger: 19 latest verdicts had a section outside their
  primary or had the old caveat. Two are confirmed (*Persona*: Asked; Snail's House – *Sunday*:
  Detectors), so 14 lose the Key caveat Collection on their next staging pass, and *Persona*,
  *Sunday*, Miike Snow – *Genghis Khan* (modal primary) and the Yoko Shimomura *monstown* track
  (LOW) keep it. Two MEDIUM verdicts lean on an unconfirmed section and would be capped at LOW
  if judged under this rule: R-906 – *The Hole* and The Belle Stars – *Iko Iko*. Neither is
  re-judged here.
- "Two of three" for the detectors is untested against corpus truth; it is the issue's rule and
  stays until a measured case says it is too strict or loose.
- A section has no end, so a window is "inside" one by its name or by lying before the next
  section's start; a verdict whose sections have no `start` can be confirmed by a source, a named
  window or the maintainer only.
