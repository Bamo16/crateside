# Handoff 19: Partners pairs by notes

Brief 19, issue #125, ADR 0056 · built against `500b721`
Canvas: `Crateside Partners By Notes.dc.html` (1a, live). Its *Canvas state* pills switch between
C major, A Dorian, A Dorian at −1 to +3 and at +3 to +5, A Dorian under Re-Pitch −1, a song with
sections, a mixed verdict, and a key by hand. The shift chips, the section pills and the tonic
and mode selects all work. The rows are computed with ADR 0056's rule and reproduce the brief's
two tables row for row. Replaces handoff 17's *1d*.

**The rule:** a plain key loses the score, the mood names and the relative pill, gains notes
shared and a shift span, and nothing else moves. A modal source adds the arrow to the basis line
and its own row at the top. Nothing is newly coloured.

## 1. The table

Columns: **Key · Camelot · Shift · Notes shared · Alt**. *Relationship* and *Score* go, with the
meaning line under the relationship.

- **Key** (Sans 14/500): the full name, `G major`, `E minor`, and a modal key in full, `A Dorian`.
  After it, 8px gap, at most one mark:
  - **same key**: filled pill, `--text` at 85%, `--ink` text, Sans 11/500, `padding 1px 7px 2px`.
    Replaces today's green: green means *done* (DESIGN.md), and this is a fact.
  - **same notes**: the same pill outlined, 1px `--text` at 45%, text at 85%.
  - **the tag** (modal source only, on the collapse's row, *A minor* for A Dorian): plain text,
    Sans 12 `--text-50`, no pill. It answers "which row is the filter my samples carry?" without
    promoting it: it is an ordinary 6 of 7 row and sorts as one.
  - The labels apply **only at shift 0**. A same-notes key listed at +5 in a +3 to +5 span is
    unlabelled; *the tag* is a fact about the key and stays at any shift.
- **Camelot** (mono 13, `--text-75`). A modal row shows its tag's code at `--text-40` (A Dorian →
  `8A`), since the wheel has no mode segments.
- **Shift** (mono 13): `0` at 400; signed figures (`+3`, `−1`, with a real minus) at 500, as the
  shifted group is today.
- **Notes shared**: seven 8px pips (radius 2, gap 3) then the figure, mono 13/500, `6 of 7`.
  Held pips filled `--text` 85%; the rest a 1px `--text-30` outline. `7 of 7` is `--text`, the rest
  `--text-75`. The pips are for scanning a column of near-identical figures; the figure is what
  is read. One `role="img"` with `aria-label="6 of 7 notes shared"` covers the pips.
- **Alt** (mono 12.5, `--text-50`): the other shifts, comma-separated, as built.

**Groups**: two caps sub-heads, as built, renamed: **As is · shift 0**, then **A shift away ·
pitch the sample by Shift to land on {source}**. A group with no rows is not drawn (a span of
+3 to +5 has only the second). Labelled rows do not get a group of their own: they sort to the
top of *As is* anyway, and the label is enough.

**Sort**: |shift|, then notes shared (descending), then same key, same notes, the tag, then
nearest on the wheel to the tag, clockwise first, major first. The last three only order rows
the rule leaves tied, and are the canvas's; keep the build's tie-break if it has one that
reproduces the brief's tables.

## 2. The shift span

Replaces Partners' ±N slider, under the basis box. One row:

`Shift` (13px `--text-60`, 40px) · thirteen chips **−6 … +6** · the span (mono 13/500,
`0 only`, `+3 only`, `−1 to +3`) · the count (`9 keys`, 12.5px `--text-50`).

- Chips: 38×30, radius 6, mono 12.5. Off: `--raised`, 1px `--line`, `--text-50` (the `0` chip at
  `--text-75`, as the anchor). In the span: `--text` at 9% fill, 1px `--text` 50% border, 600
  weight; **the two ends** at 18%, so the span reads as a bar with ends. `aria-pressed` on each.
- **Clicking**: with a single shift chosen, clicking another stretches the span to it. With a span,
  clicking any chip collapses to that chip alone. So 0 → +3 is one click, and −1 to +3 → 0 to +3
  is two (0, then +3). A hint line under the chips says so: *Click a shift to work at it alone;
  click another to stretch the span to it.*
- The span need not hold 0. **Default `0 only`.** The chips range to ±6 because the alternative
  shifts do (+3 to +5 has 18 rows for A Dorian).
- Under the table, one hint at 12px `--text-50`: at `0 only`, *Every key reaches every other
  within ±2 at its smallest shift; widen the span to see them.* Otherwise, *A key is listed once,
  at its in-range shift closest to 0. Alt holds its other shifts, a fifth either way.*
- Query: `&lo=&hi=` (both omitted at 0).

**Re-Pitch keeps its ±N slider.** It answers a different question (every shift's BPM, then pick
one row), so a span would add a step there and buy nothing. Matching the controls would make them
look interchangeable when they are not.

## 3. The basis box

- Line 1: caps *Working from*, then for a **modal** source the lead in mono 15/500 `--text` with
  `→` at `--text-60` (`A Dorian →`), then the tag 20/600 (`A min`), then Camelot (mono 14
  `--text-60`, `8A`). A plain source is as built: `C major` · `8B`. Mixed: `A mixed → A min`.
- Line 2 (13px `--text-70`), the source in one sentence:
  - `The track's verdict.` / `The key you picked.`
  - Re-Pitch: `Re-Pitch −1 from the track's A Dorian.` plus **Back to A Dorian** (`.outline`,
    small), as built.
  - Sections: `The verse, the primary centre: the tag follows it.` /
    `The chorus's centre, not the primary.`
- Line 3, modal or mixed only (12.5px `--text-60`): `ModeText`'s reason (handoff 17):
  `Dorian is a minor mode, so it is tagged by its tonic. Same notes as G major (9B).`
  Mixed: `Major melody over minor harmony; tagged by the harmony, and paired as its tag.`
- **The relative pill goes.** For a plain key the relative is now the second row, labelled
  *same notes*, at shift 0, so the pill only repeated it. Rebasing on it gave a different list,
  and ADR 0056 turned down rebasing on a same-notes key. For a modal source the pill's gloss was
  false. No third pill (handoff 17 *1d* is superseded). Drop `&rel=1`.

## 4. The section selector

In the basis box, under a hairline (`--line`, 10px above): caps *Section* (64px) and one pill per
section in the order the ledger holds them: `verse A Dor primary` · `chorus E min` ·
`bridge C Lyd`. Label in Sans 12.5, key in mono (the Tracks abbreviation for a mode), `primary`
at 11px `--text-50` on the primary's pill only. The chosen pill is the house *chosen* style for
pills (`--text` 14% fill, 50% border, 600); the primary is chosen by default.

Picking a section makes its centre the source: line 1, line 3, the table and the wheel all
follow, and any Re-Pitch shift returns to 0 (it was a shift of the previous source). Query
`&section={index}`, omitted for the primary.

**In the basis box, not the *Working on* card**: the card says what the track is; the box says
what Partners works from, which is what a section changes. Only drawn when the verdict has
sections.

## 5. Working on: the key picker

The *From the track* / *Or by hand* box's Key select becomes **two selects**: **Tonic** (72px,
12 entries, spelled for the mode's quality, mono) and **Mode** (`major`, `minor`, then an
optgroup *Modes*: Dorian, Phrygian, Lydian, Mixolydian, Locrian). Ionian and Aeolian are not
offered: they are major and minor. The mode starts at the track's centre, or `major` with nothing
picked, so the common case is one select. Editing either drops the track, as built. Not one list
of 84: two short lists find any key in two choices, and keep the five rare modes out of the way.

The card's key line uses the arrow (`A Dorian → A min · 8A`). Query `&key=` takes the mode
(`a-dorian`).

## 6. The wheel

Unchanged. *Working from* fills the tag's segment (8A for A Dorian); the shift-0 rows light at
22% as now. A modal source therefore lights 9B and 7A (G major, E minor) as mixes as is, which is
right. Drawn as a placeholder on the canvas.

## 7. States drawn

C major plain · A Dorian · A Dorian at −1 to +3 · A Dorian at +3 to +5 · A Dorian under Re-Pitch
−1 (G♯ Dorian) · sections (verse A Dorian, chorus E minor, bridge C Lydian, invented) · mixed
(A mixed → A min) · by hand (F♯ minor, no track). Narrow layouts follow handoff 12 §8 unchanged:
the chip row wraps after the chips below 860px.

## 8. After building

Update `docs/design/DESIGN.md`, *Tools*: Partners' columns, the two labels and *the tag*, notes
shared as pips and figure, the shift span and its click rule, the section pills, the tonic and
mode picker, and the relative pill gone. Clear the *Differs from the design* entries for
handoff 12 that name the relationship names and the ±0 to ±2 slider. Record the chip span as the
house control for a range of shifts.

Index handoff 19 in `docs/design/ARCHIVE.md`, and mark handoff 17's *1d* superseded there.
