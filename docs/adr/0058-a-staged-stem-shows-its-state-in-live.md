# 0058 — A staged stem shows its state in Live

- Status: Accepted
- Date: 2026-10-17
- Amends [ADR 0043](0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md): crateside
  may rewrite a sidecar the user saved, to set its colour and nothing else. Builds
  [#130](https://github.com/Bamo16/crateside/issues/130).

## Context

Since starter warps (ADR 0047, ADR 0048), a stem reaches staging in one of several states: a
starter with its marker on a beat, a starter holding only the tempo, a warp lent from a sibling,
or nothing. Live does not show which, so the maintainer had to switch to crateside to learn what a
stem dragged into a set needs.

Live has two places crateside can write to:

- **The clip colour.** Live applies the `ColorIndex` saved in a sidecar's default clip when the
  sample is dragged in, whatever the random-colour preference. MEASURED in #130: three starters
  given 0, 14 and 27 came in pink, red and light grey. `ColorIndex` numbers the 70-colour palette
  row by row, 14 to a row, from 0 at the top left.
- **The browser's seven Collections.** A file's Collections are in the folder's tag store, as
  `ablFR:colors`, a bag of numbers 1 to 7 beside `ablFR:keywords`. MEASURED in #130. Live's index
  holds them as `files.colors`, a bitmask with bit *n* for Collection *n* and bit 0 always set.
  MEASURED here: the three labelled staging files read 237 (2, 3, 5, 6, 7), 5 (2) and 3 (1).
  Live keeps the Collections' names in `Library.cfg`, which it rewrites on quit, so crateside
  cannot name them; the maintainer named them by hand.

## Decision

**The warp state is a Collection; the clip colour says only the stem's kind.**

| Collection | name | crateside puts a staged stem in it when |
|---|---|---|
| 1 red | Mine | never: the maintainer's |
| 2 orange | Check beat | it holds a starter on the grid |
| 3 yellow | Place beat | it holds a tempo-only starter |
| 4 green | Warped | it holds a warp the maintainer saved, or one lent from it |
| 5 blue | Warp by hand | it holds no starter: its tempo drifts or is unread |
| 6 purple | Key caveat | its verdict is modal, LOW or withheld |
| 7 gray | Odd bars | its track has a bar that gains or drops beats |

- **2 to 5 are one per stem, in staging only.** A stem whose sidecar is with Live (#97) keeps the
  state it had until Live is done. A starter is *on the grid* when its first marker is on beat 1,
  which is where crateside puts it once the transients place a beat (ADR 0054).
- **6 and 7 are marks on every stem of the track**, in staging and the library, since they are
  what the maintainer wants to see while picking a sample. 6 ships with the verdict's half and
  gains #128's (a key change) when that mark exists. **7 waits on #86**: until a track can be
  marked, 7 is not crateside's, and one put there by hand stays.
- **crateside changes only the Collections it owns**: 2 to 6 today. Any other the file is in
  stays.
- **On import, 2 to 5 go.** A file moved into the library arrives keyed in the index alone (ADR
  0023), and backing writes its entry. That one write now also carries its Collections: the
  ones the index holds, without the warp states and with the track's marks. An entry already
  backed is kept current in the same pass. Collections are written only onto an entry that has a
  key or is getting one, because an entry without one would make Live drop the key the index
  holds.
- **Staging is written once a minute**, by the warp worker, in one write of every change.

**Clip colours: the hue says the stem's kind; the shade keeps two songs apart.**

| kind | cells (row, column, from 1) |
|---|---|
| Drums | r1 c2 |
| Vocals | r1 c4–c6, r2 c4–c5, r3 c4–c6 |
| Instrumentals, karaoke and the kept source | r1 c8, c9, c11, c12, r2 c7–c9, c11 |

- The shade is picked from the song's upload ID, so it is stable across runs and machines, and
  two songs' stems of one kind usually differ (1 in 8 collide). The clip's name already says which
  song a stem is from, so nothing tries to make a song's vocal and instrumental match.
- **crateside owns the colour of every staged sidecar with a saved default clip**, the
  maintainer's included: they recolour clips in an arrangement and never save a default clip for
  its colour. On a sidecar it did not write, crateside changes `ColorIndex` and nothing else; AbleKit
  writes a sidecar back byte for byte. A sidecar crateside wrote stays crateside's after a
  recolour. A sidecar without a saved clip is left alone: whether Live keeps the manual flow for a
  sidecar holding only a colour was never tested, and nothing now needs it.
- A cell whose clip label turns white is swapped in the table; nothing else changes with it.

## Consequences

- **What Live does with a Collection crateside writes into the store is not yet seen.** Keywords
  written there reach the index (ADR 0017), and Collections sit beside them, but the first write
  is the test. If the browser does not show them, the store is the wrong place.
  **Amended 2026-10-17: MEASURED.** On the first run the browser showed every staged file's
  Collections within the minute, with *Mine* kept beside a warp state (Daft Punk's drums) and
  *Odd bars* kept where the maintainer had put it (the GYARI instrumental).
- **The import is inferred, not watched.** That the move carries Collections in the index, as it
  carries keywords, and that the next write drops the warp states, is ADR 0023's mechanism applied
  to a field it was not measured on. A Collection the maintainer adds by hand to a library file
  goes into the store through Live, and stays.
- **Collections in the store before this ADR are overwritten** where crateside owns them: the
  maintainer's test labels on three staged files took the old meanings.
- **A stem's state shows within a minute of changing**, a library file's within a backing pass.
