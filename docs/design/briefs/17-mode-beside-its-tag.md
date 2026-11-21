# crateside: a verdict's mode, shown beside the tag Ableton gets

Issue: #114

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against the commit that adds this brief. The rules as built are in `docs/design/DESIGN.md`
(Tracks table, Side stack, Color means state, Type), and your canvases are in
`docs/design/canvases/`; *Crateside Track Page* is Track details and *Crateside Unified Page* is
the Tracks table. The terms (tonal center, mode, section, mixed, collapse) are in `CONTEXT.md`
under *Keys*; ADR 0055 is the decision behind them. Nothing below is built yet.

---

## Where it stands

A verdict used to be a key: `A minor`. It is now a **tonal center**, a tonic and one of the seven
diatonic modes (*A Dorian*), and the 24-key tag Ableton is written with is **computed from it** by
the *collapse*, which keeps the tonic: A Dorian is tagged **A minor**, G Mixolydian **G major**.
The tag is never stored as the truth. The maintainer does not keep the mode-to-quality table in
mind, so wherever a key is shown and the verdict is not plain major or minor, the derivation has to
be visible, and the tag must never stand alone.

Only a human source can establish a mode (every detector reports major or minor), so a modal
verdict is rare. Measured on the existing ledger, latest verdict per song, 519 songs (2026-10-16):

| | songs |
|---|---|
| plain major or minor | 496 |
| Dorian | 12 |
| Mixolydian | 5 |
| Phrygian | 3 |
| a mode recorded as `major` (becomes Ionian) | 3 |

Sections and *mixed* have no history yet; they start with the judge rewrite. About 4% of songs
will show a mode, so **the 96% that do not must look exactly as they do today**, with nothing added
to explain a mode that is not there.

## Where a key is shown today

| place | what it shows now |
|---|---|
| Tracks table, *Key* and *Confidence* columns | the tag in mono, `A♯ min`, then the confidence (`HIGH`, `MEDIUM`, …) in its own column |
| Track details, the band under the title | cell *Key*: the tag in large mono; the sub-line says `really A Dorian, tagged as A min` for a modal verdict (plain text, the one existing hint of this) |
| Track details, *Why this key* | the sources, each with the key it stated and, in brackets, its mode: `D minor (dorian)` |
| Partners and the Camelot wheel (Tools) | keys by their tag; the wheel is read-only |
| Dashboard, *Key detectors* | how often each detector matches the verdict, as a tally; it compares the tag |

The tag is spelled `A♯ min` / `E♭ maj` on screen (`KeyText.Spell`), in IBM Plex Mono.

## What the verdict can now hold

| | |
|---|---|
| **Tonal center** | tonic + mode: *A Dorian*, *E minor*. The tag is its collapse |
| **Sections** | optional; each a label with its own center: *A Dorian (verse), E minor (chorus)*. The verdict's primary center is the one the tag follows |
| **Mixed** | two modes on one tonic at once, such as a major melody over minor harmony. The tag follows the harmony |
| **Sources** | each states a key, optionally a mode, and optionally the sections it covered ("Chorus only") |

## What the user needs

- **See the mode beside the tag** wherever the verdict is not plain: *A Dorian → A minor*, in the
  mono face, so it reads as one fact and not a tag with a footnote.
- **Understand the arrow on hover or tap.** The text the issue proposes: *Dorian is a minor mode,
  so it is tagged by its tonic. Same notes as G major (9B).* The same-notes key is the major key
  whose scale has the same seven notes. **It is not always next to the tag on the Camelot wheel**
  (the issue first said so; it is wrong), so the hint names the key and its Camelot code and says
  nothing about distance:

  | mode | tagged as | same notes as | Camelot (tag → notes) |
  |---|---|---|---|
  | Ionian | A major | A major | 11B → 11B |
  | Lydian | A major | E major | 11B → 12B |
  | Mixolydian | A major | D major | 11B → 10B |
  | Aeolian | A minor | C major | 8A → 8B |
  | Dorian | A minor | G major | 8A → 9B |
  | Phrygian | A minor | F major | 8A → 7B |
  | Locrian | A minor | B♭ major | 8A → 6B |

- **Find the other centers of a song with sections.** The primary shows where the key always
  shows; the others are reachable on the track (Track details), each with its label.
- **Be told when a song is mixed**, and that the tag follows the harmony: *Major melody over minor
  harmony; tagged by the harmony.*
- **Trust the tag is still the one Ableton has.** The tag is what is written to the samples; the
  mode is the truth about the music. The user may be looking at a tagged sample and a verdict at
  the same time.

## Questions for the design

- **The Tracks row.** The *Key* column is narrow and the row is dense. Does a modal verdict add the
  mode beside the tag (a longer cell for 4% of rows), mark the tag (a small glyph, detail on
  hover), or something else? The mode must be findable without opening the row.
- **How loud is it?** Color means state, so the mode is monochrome. Is weight, a rule, or a
  prefix enough to say *this key is not what it looks like*?
- **Track details' band.** The *Key* cell's value is the large mono figure with a sub-line. Does
  the arrow form (*A Dorian → A minor*) replace the figure, or does the tag stay large with the
  mode above or below it?
- **Sections.** A song with three centers: what does the Track details band show of them, and what
  does the evidence section show? A section list with labels, or the centers laid on the track's
  time axis the way the warp view lays lanes?
- **Sources and sections.** A source that analyzed only the chorus should not read as a dissent
  from a verdict whose primary center is the verse. How does *Why this key* show which sections
  each source covered, and when one source disagrees with a section, not the song?
- **Mixed.** One line, or a mark on the figure? It is rare and subtle.
- **Partners and the wheel.** Partners works from the tag. For a modal verdict, is the tag all the
  tool should know, or should the same-notes key be offered as a second basis? Say so if it is
  worth building; nothing is asked of it in this issue.

## Leave room for

- A mode shown on the Dashboard's tallies later; nothing to design for it now.
- A fourth verdict shape: *no tonal center* (atonal, drone, percussion-only). Not in the model
  yet, and not wanted in this design, but do not box it out.
