# crateside: Partners pairs by notes

Issue: #125

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

This brief was written against the commit that adds it. The rules as built are in
`docs/design/DESIGN.md` (*Tools*). The page today is *Crateside Tools Together* in
`docs/design/canvases/` (handoff 12). The decision behind this brief is
[ADR 0056](../../adr/0056-partners-pairs-by-notes.md), and the terms (tonal centre, section,
mixed, collapse, same-notes key, notes shared, parallel) are in `CONTEXT.md`. Brief 17 and its
handoff showed a verdict's mode beside its tag. This brief builds on them and replaces handoff 17's
*1d Partners* proposal.

---

## Where it stands

Partners answers "what keys can I play over this?". The user filters Live's browser by those keys
and auditions by ear. It was ported from an old script, and the port kept everything: a score, six
mood names for a partner (*Perfect Match*, *Relative (Epic)*, *Energy Rise*, *Relaxed*,
*Mood Shift*, *Modulation*), and a ±N slider defaulting to ±1. The user reads only two of those
labels, same key and relative, and sorts by shift.

A verdict can now be **modal** (*A Dorian*). Its tag is the collapse (*A minor*), but its notes are
G major's. Partners never saw the mode, so it offered the tag's partners and told the user to pitch
G major up 2 semitones, off the very notes the song plays.

How the user pairs, in their words: one vocal over one instrumental, by ear. They try the same key
and the same notes first, and move on if neither works. They go wider for an instrumental they are
set on, and use Re-Pitch (speed and pitch together) occasionally.

## What changes (decided)

- **Every row shows notes shared**: how many of the source's seven notes the partner holds once
  shifted, as `6 of 7`. Only two labels remain: **same key** and **same notes**. The score and the
  mood names go.
- **Rows sort by shift, then notes shared.**
- **The shift range defaults to 0** and can be asymmetric, such as −1 to +3.
- **The source is the verdict's centre**, mode included. A key picked by hand is a **tonic and a
  mode**, replacing today's 24-entry dropdown of major and minor keys.
- **A song with sections offers each section's centre as the source**, with the primary chosen by
  default. Sections are shown in the order the ledger holds them, since they have no times yet.
- **A modal source's own centre is its same-key row**: *A Dorian*, 7 of 7, first. Live's browser
  will find it once the mode is written as a Key tag (#127).
- **A mixed verdict** pairs from its tag.

Partners still returns **keys** to filter for in Ableton's browser, never files.

## Example rows

Measured on the crateside code with the rule above, at shift 0.

**C major** (plain, 96% of songs):

| key | shift | notes shared | label |
|---|---|---|---|
| C major | 0 | 7 of 7 | same key |
| A minor | 0 | 7 of 7 | same notes |
| G major | 0 | 6 of 7 | |
| F major | 0 | 6 of 7 | |
| G minor | 0 | 5 of 7 | |
| C minor | 0 | 4 of 7 | |
| F minor | 0 | 3 of 7 | |

**A Dorian** (tag A minor):

| key | shift | notes shared | label |
|---|---|---|---|
| A Dorian | 0 | 7 of 7 | same key |
| G major | 0 | 7 of 7 | same notes |
| E minor | 0 | 7 of 7 | same notes |
| A minor | 0 | 6 of 7 | |
| C major | 0 | 6 of 7 | |
| D major | 0 | 6 of 7 | |
| D minor | 0 | 5 of 7 | |
| A major | 0 | 5 of 7 | |
| E major | 0 | 4 of 7 | |

## Questions for the design

- **The table.** What do the columns become once *Relationship* and *Score* are gone? Do labelled
  rows stand apart, or is the label enough? `7 of 7` repeats on every row, so does it need a
  visual form (a bar, pips) or is the figure better?
- **The tag's row on a modal source.** *A minor* is not A Dorian's key, but it is the tag most
  of the user's samples are filtered by, and it sits at 6 of 7. Does it need marking as the tag,
  or is notes shared enough?
- **The shift range.** It defaults to 0 and is asymmetric. Two handles on one track collide at 0.
  An alternative is a row of shift chips (−6 … +6) where the user can pick a span, which also
  handles 0 alone. Choose, or propose something better. Re-Pitch has a ±N slider of its own;
  should the two match? One fact the range must respect: every key reaches every other within
  ±2 at its smallest shift, and its other shifts are a fifth either way (±7, wrapped to ±6). A key
  is in range if any of its shifts is. It is listed once, at its in-range shift closest to 0, and
  the *Alt* column holds the others. So a range of +3 to +5 still has rows.
- **The relative pill.** The basis box has two pills today, the key and its relative. Same-notes
  keys now sit at shift 0 anyway. Does the relative pill stay, change, or go?
- **The key picker.** A tonic and a mode, or one list of 84 entries? The mode must default to
  major or minor, since modes are rare.
- **The section selector.** Every section visible at once, the primary chosen, in the order held.
  Each entry needs its label and its centre (`verse · A min`, `finale · A♯ min`; an invented
  example). Where does it sit: in the basis box, or the *Working on* card?
- **Mode beside the tag.** The basis line names the mode the way handoff 17 does
  (`A Dorian → A min`).

## Constraints

- **Plain keys change least.** About 96% of songs are plain major or minor (brief 17's count). For
  them, the table loses its score and mood names, gains notes shared, and nothing else moves.
- **Colour means state** (DESIGN.md). Notes shared is a fact, not a state.
- **The Camelot wheel stays** read-only, as built.

## Not asked

- Lists of library tracks, per-key track counts, the BPM a shifted partner needs, and grouping
  rows by the browser filter that finds them. All were considered and not taken (ADR 0056).
- A section's times on a time axis: sections have none yet.
