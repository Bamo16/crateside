# Handoff 07: the expanded row's proportions, and genre from the collapsed row

Issue: #65 · answers `briefs/07-row-proportions.md` · built against `c9c5e4b`
Canvas: `Crateside Row Proportions.dc.html` (1a–1g, 2a–2b), which mounts `Crateside Row Stack.dc.html`.

The canvas is a design reference in HTML. Port it to the Blazor components named below. No new
tokens, nothing newly colored, and buttons stay `.outline` / `.primary` / `.danger`.
Song names, keys, BPMs and "heard" notes on the canvas are illustrative. Preset names come from
the test catalogs; the real ones come from the StemForge catalog.

## 1. The open row: spend width, not height

At `--table-tracks` the samples column is about 1100px wide and its lines are mostly empty. So:

### The row's grid changes by mode

| mode | grid (`.row-panel`) | right side |
|---|---|---|
| closed, planning (before separation), no samples | `minmax(0,1fr) 680px`, gap 32px | `SideStack` in **two columns** |
| picking after separation | `minmax(0,1fr) 340px`, gap 32px | the stack **folded**: only StemConfirm, or the add/adopt form |

Below about 1200px viewport, fall back to `minmax(0,1fr) 340px` with a one-column stack, as today.

### SideStack in two columns (1a, 1b, 1c, 1f)

- `grid-template-columns: 1fr 1fr; gap: 0 28px; align-items: start`.
- Left column: **Go → Next → Tags**. Right column: **Name → the stronger rule → Start over**.
  The order is handoff 06's, wrapped once. Confirm (slot 0) never appears here, because picking
  after separation folds the stack (below).
- Tags label width goes from 78px to 88px, so *Familiarity* fits. Familiarity's numbers are
  indented to the value (98px).
- New parameter: `SideStack Columns="1|2"`. The row passes 2; Track details passes 1.

### Closed list (1a, 1b)

- Unchanged apart from widths: `grid-template-columns: minmax(0,1fr) 110px 130px`, plus the note
  column only when a line has a note. The dashed **Change what is cut** opener stays as built.
- The canvas-06 "Samples label line with three acts" is **withdrawn**, so clear that entry from
  "Differs from the design".

### Picking: bands side by side (1c, 1d, 1e)

- The bands sit in a grid: `grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px 14px;
  align-items: start`. Use `auto-fit`, not `auto-fill`: four bands must share the width, with no empty track. Each band is its label (as built, `.stem-band`) over its lines.
- **Compact picking line**: `16px minmax(0,1fr) auto`, gap 10px, padding 7px 10px, radius 6.
  It holds the tick, the preset name (13px 500, line-height 1.35, **wraps**, never truncated: the name is the only thing telling presets apart) and **one state word** in mono 11.5px:
  *Staging* (`--text-60`) · *Library* (green) · *new* (teal) · *removed* (red, name struck through)
  · *not made* (`--text-30`). Line tones (coming, going, unmade, guide) are as built.
- Dropped while picking: the key column and the long notes. The key is on the row header, and
  StemConfirm's sentence says what goes and what is made. A preset's description stays on hover.
- A source line reads **Source**, not *{title} (Source)*. An added source reads its label.
- The Karaoke and Warp guide bands may share a column (`Karaoke · Warp guide`) when a band has a
  single line. This is optional, so do it only if the band model makes it cheap.
- Planning (before separation) lines stay tick + label, and the stack stays live in two columns.

### Picking after separation: the stack folds (1d)

- The right side is 340px and holds **StemConfirm** once a tick has moved. Below it, a hairline
  and *The rest of this row is back once you confirm or cancel.* in 12px `--text-50`.
- With no tick moved, the right side shows one sentence, *Tick what should exist. Nothing changes
  until you confirm.*, and a **Close** `.outline` button. The full-width Close line under the
  list goes.
- Go, Next, Tags, Name and Start over are **not rendered** while folded (today they are dimmed and
  inert). `TrackEdits.Close()` still runs when the picker opens.
- New parameter: `SideStack FoldWhilePicking`. The row passes true; Track details passes false.

### Add a source / Adopt a file (1e)

- The form moves to the **right column**, where a confirm goes. Its fields stack at 340px: *Where it
  is*, *What it is* (or *The file* / *What the track calls it*), then *Plays as* pills, the note, and
  `[Add source | Adopt it] [Cancel]`. The styles are as built (`.add-source`); only
  `.add-fields` changes, to a single column.
- The opener that opened the form stays in the Sources band, with an amber border and text reading
  *Adding a source →* / *Adopting a file →*. The other opener dims to 45%.
- If a tick has also moved, StemConfirm sits above the form in the same column.
- This needs StemList to render the form into the side slot. Either StemList takes a
  `RenderFragment`-style split (`Part="List|Side"`, like TrackEdits), or the row renders
  `StemEditor`'s form component in the side slot. The implementer chooses.

### No samples (1f)

Unchanged. The two-column stack is what keeps it short.

### Track details (1g)

- The rail stays one column and is not folded. The left column is long, so the rail is never the
  taller side.
- The band grid applies there too, since StemList carries it in both places. It is about four
  across at 1100px.
- StemConfirm stays at the top of the rail with the rest dimmed, as built. The add/adopt form
  stays inline in the Sources band on Track details. The side slot is a row-only thing.

## 2. Genre from the collapsed row

### A Genre column (2a)

- Columns become `24px 1fr 90px 100px 80px 150px 190px`. **Genre** goes between BPM and State,
  and Song gives up the 150px. The table cap is unchanged.
- Cell states:
  - **Suggestion**: the stack's amber dashed chip (12px, `3px 10px`, radius 999). **Clicking it
    accepts** (`Genres.Settle`, logged as `GenreSettled`). The click must not toggle the row
    (`@onclick:stopPropagation`). Accepted, it becomes plain text in place.
  - **Settled**, or learned from the artist: plain text, 12px `--text-60`.
  - **No answer, no suggestion**: an amber dashed chip reading *choose* (border at 60%). Clicking
    it expands the row with Tags open.
  - **Not looked up**: empty.
- Familiarity is **not** in the ordinary collapsed row. Of the 115 that ask, none has a
  suggestion to accept, and a derived value is already settled.

### A "Tags to answer" filter (2b)

- A sixth filter pill, between *Needs attention* and *All*: **Tags to answer**. It holds every named
  song whose genre is unanswered, or whose familiarity asks the user. Add a count if the other
  filters carry one.
- In this view the collapsed row's columns change to `24px minmax(0,300px) minmax(0,1fr) 220px`:
  - **Song**, with a 12px `--text-50` note under it: the genre note as built (*heard: …*,
    *the sources know nothing of it*), plus *your call on familiarity* when it asks.
  - **Genre**: every chip inline, via the same `ChoiceStrip`, with the suggestion amber dashed.
  - **Familiarity**: *Mainstream / Obscure*.
  - A column with nothing to ask shows its settled value as plain `--text-60` text.
- One click on any chip settles that tag, so a wrong suggestion also costs one click.
- An answered row **stays in place** until the user leaves the filter or the list refreshes, so
  nothing moves under the cursor. A fully answered row drops to 45% opacity. The chevron still
  expands the row as normal.
- Not designed: a bulk "accept every suggestion". Ask the user before adding one.

## Parameters

| component | new | row | Track details |
|---|---|---|---|
| `SideStack` | `Columns` | 2 | 1 |
| `SideStack` | `FoldWhilePicking` | true | false |
| `StemList` | side slot for StemConfirm and the add/adopt form | yes | no (inline, as built) |
| `ChoiceStrip` | `Inline` (no label, no note: the note moves under the song) | Tags to answer view | — |

## DESIGN.md after building

- **Side stack**: two columns in the row, folded while picking after separation.
- **StemList**: bands in a grid, and the compact picking line.
- **Tracks table**: the Genre column and the *Tags to answer* filter.
- Clear the handoff-06 "StemList's acts are unchanged" entry, since the canvas now matches the build.
