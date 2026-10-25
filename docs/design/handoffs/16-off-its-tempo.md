# Handoff 16 · a warp that sits off its tempo, and True up

Brief: `docs/design/briefs/16-off-its-tempo.md` · Issue #96 · ADR 0050 · canvas `Crateside Off Its Tempo.dc.html` (1a–1d).
Figures, offsets and dates in the canvas are made up; songs in italics are not real. The canvas's lanes are simplified (taller gutters, a short *Borrowed* chip with the sibling in the sub line); the lanes as built in handoff 14 are unchanged.

## The principle

**Where a problem shows depends on whose problem it is.** *Drifts* is the music's, so it stays a flag
beside the title. *Off its tempo* is the warp figure's, so it moves next to the BPM as a mark. No new
colors: the mark is monochrome. Only the confirm (red), writing (teal) and the confirm label (amber)
use color, as the other warp controls do.

## The mark (1a, 1b)

A **pennant**, the warp view's marker shape (7×9, notched right edge, `--text` at 80%), drawn only for
*off its tempo*:

- **Filled**: 10 ms or more by the end. Worth truing up.
- **Hollow** (1px stroke, `--text-60`): measurably off, under 10 ms.

**Tracks BPM column**: an approximate figure is whole and already carries an invisible `.00`. The mark
sits in that slot, 4px after the figure, so the column stays 84px and the digits stay aligned. The
cell's title is the full note, as now. **Remove the `off its tempo` chip beside the title** (and with it
the missing-gap bug). `drifts N%`, `part changes tempo` and `changes tempo` stay as they are.

**No mark** for `steps in its grid` or `grid not confirmed`: the `~` already says approximate, and the
pennant means one thing only, *crateside can fix this for you*. A tight reading that disagrees with a
warp that holds (`crateside reads 99.98`) keeps its bare figure and gets no mark.

## Tracks: an *Off its tempo* filter (1a)

Beside *Tempo drifts*, with its count. Rows are steady warps whose grid falls away from the audio,
**sorted by the drift, furthest first**. The columns change: Song · Key · BPM · **Runs** (`0.017 slow`,
mono 13 `--text-75`) · **By the end** (`27 ms`, mono 13/500, right-aligned; under 10 ms at
`--text-55`) · State. Gist right of the pills: `steady warps whose grid falls away from the audio ·
furthest first`. **The later batch action goes at this filter's head**; nothing is drawn for it now.

## Track details: the BPM cell (1b)

Headline `~125` with the mark after it (11×14, top-aligned). The note stays short, `off its tempo ·
from your warp`. Under it, **one sentence that names both figures** (13px `--text-75`), so the user can
see it's their warp being questioned:

| state | sentence | link |
|---|---|---|
| off its tempo | `Your warp is typed at 125. The audio holds 125.017, so its grid falls 27 ms behind by the end.` | `True up in Warp ↓` |
| off, under 10 ms | `Your warp is typed at 98. The audio holds 98.002: 4 ms by the end, too little to hear.` | `True up in Warp ↓` |
| steps in its grid | `Your warp holds one tempo, but the beat jumps partway through. That takes a marker in Live where it jumps, not a retype.` | — |
| grid not confirmed | `Too few transients to check your warp against. The figure is the one you typed; crateside can't vouch for it.` | — |
| reading disagrees | `Your warp is typed at 100. crateside reads 99.98.` | — |

The fast/slow wording: `slow` when the typed figure is below what the audio holds (the grid falls
behind), `fast` when it is above (the grid runs ahead). The full note with the amounts stays the
tooltip everywhere else. No tempo strip for these states; the warp view draws the drift.

## Warp view: the *Off the grid* row (1c)

A new row **between the tempo line and the first stem lane**, in the same 200px gutter · plot grid.
It belongs to the track's canonical warp, as the tempo line does, so **True up lives here, not in a
stem's detail**: selecting a lane never changes what it acts on. The stem detail keeps its two controls.

- **Drawn whenever the check ran** (any steady warp). A warp that changes tempo doesn't get it.
- **Plot** (72px): a dashed `--text-30` zero line; one dot (r 2.2, `--text` 85%) per 10 s window at the
  median offset of that window's transients from the warp's grid lines, joined by a 1px `--text-35`
  line. Scale ±30 ms, clamped; labels right-aligned, mono 10 `--text-40`: `audio early +30 ms` top,
  `late −30 ms` bottom. Same time axis as the lanes; the 10 s window holds at every zoom.
- **Gutter**: label line `Off the grid` (13/500) with the **True up** button right (`.outline`, small);
  then the drift `27 ms by the end` (mono 13/500); then `runs 0.017 slow · grid falls behind` (11.5
  `--text-50`). A warp that holds: `holds`, no button.
- **Status line** under the row, spanning the plot (13px, max 760px), in the side stack's status shape.

**States**

1. **Offered**: button shown; status `--text-75`: `Runs 0.017 slow, so its grid falls 27 ms behind by
   the end. Retypes it to 125.017 and lines it up with the drums. Yours is backed up first.`
2. **Confirm** (replaces the status; the button hides): a `--surface` box, label `True up the warp` in
   amber, then `Retype your warp from 125 to 125.017 and line its first beat up with the drums? The grid
   moves by up to 27 ms, most at the end. Vocal - Full borrows this warp and moves with it. Yours (1
   marker, saved 29 Sep) is backed up first.` Buttons `.danger` **True up my warp** · `.outline` **Cancel**.
   Name each borrower; leave that sentence out when there is none.
3. **Writing**: the canonical stem's chip turns teal `Writing`; teal dot + `Retyping the warp to
   125.017… Yours is backed up.`
4. **Done**: the dots redraw flat; the tempo line's gutter and the band read `125.02` vouched; the drift
   reads `1 ms by the end` / `holds`; status at `--text`: `Trued up just now: 125.017, within 1 ms to
   the end. Your previous warp (125, saved 29 Sep) is backed up.` (relative time until the page is left).
   The canonical lane's sub line: `trued up just now · lent to …`; a borrower's: `follows the true-up`.
5. **Not offered**: no button; status at `--text-40`, `Not offered: {reason}`. Canvas example: `Not
   offered: Vocal - Full has markers of its own past 2:10, and retyping the shared warp would pull the
   two apart.` Use the build's real reasons.

The placement follows Live's clip panel, where `/2` and `×2` sit beside the BPM box. True up is the
same kind of action: it acts on the figure, so it sits by the tempo.

## The shape says whose fault (1d)

The same row, for four steady warps. This is how the brief's questions about telling the states apart
get answered: by shape, not by more marks.

- **Off its tempo**: a straight slope. A retype fixes it → True up.
- **Drifts**: wanders both ways. No single figure fits, so the fix is a denser warp in Live.
- **Steps in its grid**: flat, a jump, flat. The fix is a marker at the jump, in Live.
- **Holds**: flat within a few ms.
- **Grid not confirmed**: no dots. Only the dashed line, with `too few transients` in the gutter.

**The milliseconds are drawn on this row only**, never on the stem lanes, which stay Live's clip view.

## Answers to the brief

- *Is a flag enough on Tracks?* The flag is replaced by a two-level mark in the BPM cell. How far off
  gets its own columns in the *Off its tempo* filter.
- *Off its tempo vs drifts?* They show in different places (BPM vs title) and draw different shapes (slope vs wander).
- *Marks for the three unconfirmed notes?* Only *off its tempo* gets one. The others stay text, and the
  Off the grid row shows them as different shapes.
- *Offset on the lanes?* No. It gets its own row above them.
- *Indicator beside the title?* Moved next to the BPM (maintainer's point).
- *Where does True up belong?* On the canonical warp's row, not under the selected stem.

## DESIGN.md changes

- **Tempo**: add the pennant mark (filled ≥ 10 ms, hollow below) for *off its tempo* only, wherever a
  BPM figure stands with its weight. Add the two-figure sentence on Track details. Clear the *Differs*
  entries for #96 once built.
- **Tracks table**: the *Off its tempo* filter and its columns; remove the title chip for this state.
- **Warp view**: the *Off the grid* row, its drawing rules, and True up living in its gutter (drop
  the third control column from the stem detail).

## Questions for the build

- **The 10 ms line** between filled and hollow is a proposal. Report the spread of drift across the
  library's steady warps, and whether real tracks cluster either side of it.
- **10 s windows**: is the per-window median steady enough for the line to read straight on a real
  off-tempo track, or does it need 20 s?
- **Steps in its grid**: does the check know where the jump is? If it does, the row can say `jumps at
  2:10` in its gutter, and the song page can say it too.
