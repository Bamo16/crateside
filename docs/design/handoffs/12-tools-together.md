# Handoff 12: Partners and Re-Pitch on one page, sharing a selected track

Issue: #79 · built against `9077ac3`
Canvas: `Crateside Tools Together.dc.html`. 1a is the page at 2560px and is live: the *Canvas
state* pills above it switch between the brief's states, and the rows, pills, card and ▾ work.
1b shows how the columns fold. 1c shows where a third tool goes.

The canvas is a design reference in HTML. Port it to one page under `ToolsFrame` (it loses its
sub-tabs), made from `Partners.razor` and `RePitch.razor` as components. No new tokens and nothing
newly coloured. Buttons `.outline`, choices `.pill`, the existing slider and select. The header
stays `Bar`. Two decimals only for arrangement BPMs and the track's BPM field; whole numbers
elsewhere. Partners' groups and scores on the canvas come from a simple Camelot model. **Keep the
built calculation** and show its rows in the two groups below.

## 1. The page

Title and lead as today ("…all working from one track"). **No sub-tabs.** Under it, one grid of
columns, gap 40px, `align-items: start`:

| column | width | holds |
|---|---|---|
| Working on | 340px | the selected track, and key/BPM by hand |
| Re-Pitch | 540px | foldable |
| Partners | `minmax(0, 860px)` | the basis box, then the table |
| Wheel | 340px | the Camelot wheel and its legend |

Re-Pitch folded removes its column: `340px minmax(0,860px) 340px`, and a one-line summary sits
above Partners (§3). No body width cap beyond this.

## 2. Working on (left)

A caps label *Working on*, then:

- **With a track**: a card (`--surface`, `--line-strong` border, radius 8, `padding 14px 16px`).
  The song (15px/500), then key + Camelot (mono `--text-50`) and "{bpm} BPM, warped" (whole
  number), then genre · familiarity · a *Track details* link (12px `--text-50`). "Change ▾" sits
  top-right. **The whole card is clickable.** It turns into the search field with its results
  open and focused, and Esc puts the card back.
- **With no track**: the search field ("⌕ Search warped tracks", as built). Results show up to 8
  warped songs: name · BPM to two decimals · Camelot. Picking one selects it for the page.
- **Under either**, a box: *From the track* (or *Or by hand* with no track), a Key select and a
  BPM field (two decimals). With a track, they show its values. **Editing either drops the
  track** (the card goes back to the search field) and keeps what was typed. A hint line says so.

## 3. Re-Pitch

Head: ▾ · `h3` Re-Pitch · gist "speed and pitch together". The whole head folds it. Then the lead
("…Pick a row and Partners works from that key."), Max shift (slider, ±3 default), and the table
as built: Shift · Arr. BPM (17px mono 500, **click to copy**, *copied* for a moment) · Lands on ·
Camelot. Highest shift at the top. The 0 row is the reference stripe, tagged *the track* or *as
typed*.

- **A row is selectable.** Clicking it (anywhere but the BPM) makes it **the shift**: 600 weight,
  `rgba(255,255,255,.08)` fill with a 1px inset ring (`--text-35`), and a small tag *→ Partners*
  after the key. Clicking the chosen row again returns to 0. With no key, rows are not
  selectable.
- The BPM cell's click copies and does not select (stop propagation).
- **Empty** (no key, no BPM): all shifts drawn with dashes, as built. **Key, no BPM**: keys fill
  and BPMs stay dashes. Hint: "No BPM yet: the keys fill, the BPMs wait."
- **Folded**: a dashed one-line bar above Partners: "▸ **Re-Pitch** −1 · 188.77 BPM · A♯ minor,
  sent to Partners", or "no shift chosen". Clicking it unfolds. **The shift stays in force while
  folded.**
- Fold state is remembered (local storage). Default open at ≥1100px, folded below.

## 4. Partners

Head: `h3` Partners · gist "keys to filter for in Ableton's browser, never files".

**The basis box** (`--surface`, `--line`, `padding 14px 16px`) says which key Partners uses:

- Line 1: caps *Working from*, then the key (20px/600) and Camelot (mono). On the right, two
  pills: **{shifted key}** · **{its relative} · relative**. That is the one-click switch to the
  relative key.
- Line 2 (13px `--text-70`), the reason, in one sentence:
  - no shift: "The track's own key." / "The key you typed."
  - shifted: "Re-Pitch −1 from the track's B minor." plus **Back to B minor** (`.outline`,
    small), which sets the shift to 0.
  - relative: "The relative of A♯ minor, which is Re-Pitch −1 from the track's B minor."
- Max shift (Partners' own slider, as built) under the box.

**The table** keeps its columns: Key · Camelot · Shift · Alt · Relationship · Score. Relationship
gains a second line (12px `--text-50`) saying what it means ("one step clockwise", "the same
notes, the other mode"), for the relationships the user can't recall. Rows are split under two
caps sub-heads:

1. **Mixes as is** (shift 0): same key (green, as built), relative, a fifth up, a fifth down, the
   relatives of the fifths (diagonal), parallel.
2. **A shift away · pitch the sample by Shift to land on {key}**: the rows with a non-zero shift,
   with Shift in 500 weight.

Sort by score inside each group. With no key, the table is replaced by a dashed box: "Pick a
track, or a key, on the left."

## 5. The Camelot wheel

A 320px wheel under a caps label: outer ring majors (B), inner ring minors (A), 12B/12A at the
top. Each segment shows its code (mono 11px) and the key's short name (10px).

- **Working from**: `--text` fill, `--ink` text.
- **Mixes as is**: `--text` at 22% fill.
- **A shift away**: dashed `--text-60` outline.
- **The track's own key**, when a shift or the relative is in force: a 2px `--text` outline, so
  the user can see where he started.
- Everything else `--surface` with `--line` and `--text-40` labels.

Monochrome, so nothing is newly coloured. A legend below, and one line: "A neighbour on the wheel
is a fifth; straight in or out is the relative." **Read-only** in this pass. Clicking a segment
to work from it is a possible later step.

## 6. How the page follows the user's scenario

1. He types "asc", picks *Xi – Ascension to Heaven*: card shows B minor · 10A · 200 BPM. Partners
   works from B minor.
2. He clicks Re-Pitch's −1 row: 188.77 · A♯ minor. Partners now says **Working from A♯ minor 3A ·
   Re-Pitch −1 from the track's B minor**, and the wheel lights 3A with 10A ringed.
3. Nothing fits, so he clicks **C♯ major · relative**. One click, with the reason line updated.
4. He reads the wheel's lit neighbours and Partners' *a fifth up / down* rows for what else to try.

## 7. States (canvas state pills)

No track, no key · a key typed, no BPM · picking ("asc") · track selected · shift −1 chosen ·
Re-Pitch folded. All are drawn in 1a.

## 8. Narrow (1b)

- **≥ 2200px**: four columns.
- **1700–2200**: the wheel moves into Partners' column, beside the basis box, above the table.
- **1100–1700**: *Working on* becomes one row across the top (card · key · BPM), with Re-Pitch
  and Partners side by side under it.
- **< 1100**: one column: Working on, Re-Pitch (folded by default), Partners, wheel.

## 9. A third tool (1c)

A foldable column with Re-Pitch's anatomy (head with ▾, lead, controls, table, hint) that reads
the selected track. It goes **left of Partners** if it changes the key or BPM Partners works from
(and the basis line names it), and **right of the wheel** if it doesn't. Partners never folds.
Folded tools stack as summary lines above Partners, in column order.

## 10. Routes

- **`/tools`** is the page. State in the query: `?track={id}` or `?key=&bpm=`, plus `&shift=`
  and `&rel=1`, so a reload or a pasted link restores it.
- `/tools/partners` and `/tools/repitch` redirect to `/tools` with their query kept.
  `/tools/repitch` also unfolds Re-Pitch. `/partners` still redirects.
- The shared-key memory across tools is no longer needed: there is one key.

## 11. After building

Update `docs/design/DESIGN.md`:

- **Tools**: one page, no sub-tabs. The columns, the selected track (clickable card, editing
  drops it), the fold rule, the shift → Partners basis box, the two Partners groups, the wheel,
  and the third-tool rule. Drop the sub-tab and "one tool per sub-tab" rules.
- Record the Re-Pitch row selection (fill, inset ring, *→ Partners* tag) as the house style for a
  chosen row in a tool table.
- Caps: Re-Pitch 540px, Partners 860px.
- Clear "Tools (handoff 09)" under *Differs from the design*. The picker already searches warped
  songs.

Index handoff 12 in `docs/design/ARCHIVE.md`.
