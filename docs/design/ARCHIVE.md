# crateside design archive

Everything produced in the Claude Design sessions for crateside, as of 2026-09-25 (repo at `7d6f916`).

These are **specs from a point in time**, not the source of truth. The code is. Where the two disagree, the code wins, unless an open issue says otherwise.

## What's here

### `canvases/`: the designs themselves
Open any `.dc.html` in a browser. They need `support.js` beside them. Each is a canvas: turns are stacked **newest at the top**, and every artboard has an id (`1a`, `2b`, …) that the handoffs refer to.

| file | turns | status |
|---|---|---|
| `Crateside Unified Page.dc.html` | 1 · Tracks page · 2 · re-cut and rename · 3 · stem selection, uploads, add track (#22) | 1–2 built, with divergences recorded in the recut brief. 3: check against code. |
| `Crateside Track Page.dc.html` | 1 · Track details, all states · 2 · actions, stage flow, embed, genre, activity log (#21) | **new**, not built. Turn 2 supersedes turn 1 where they differ. |
| `Crateside Brand.dc.html` | 1 · five marks · 2 · fifteen more | **1a chosen** (interim). The rest are kept in case the brand is revisited. |

### `handoffs/`: what was sent to the implementing agent each time
The written spec for each canvas turn, in the order it was sent. The later ones assume the earlier ones are built.

### `brand/`: the 1a assets
`mark.svg` (header), `favicon.svg` plus PNG fallbacks (browser tab), `lockup.svg` (README), `mark-tile.svg` (app icon). Usage rules are in `handoffs/04-brand.md`.

### `briefs/`: what the design sessions were asked for
The briefs written by the implementing agent. #22's brief was pasted in chat and isn't included; it's in the issue.

## Design language, in one place
The live definition is `src/Crateside/wwwroot/app.css`. The rules the canvases follow:
- One warm dark palette: `--bg #141310`, `--surface #1b1a15`, `--raised #23211a`, `--text #ece7dc`, with alpha tiers for prose (75/60/50) and for labels only (40/35/30).
- Colour means something, and nothing else is coloured. **Teal** = machine working, **amber** = your turn, **red** = needs attention, **green** = done, only ever done.
- IBM Plex Sans for text; IBM Plex Mono for every number, key, timestamp and count.
- State chips: neutral outline · teal filled with a dot · teal outline · amber filled · red outline · red filled · green outline · dashed.
- Three page-header shapes (Bar, Flush, Shade), and a closed set of them.
- One width cap per table shape (`--table-*`).

## Numbering (added 2026-09-25)

From brief 05 on, a handoff takes its brief's number: `briefs/NN-<topic>.md` → `handoffs/NN-<topic>.md`.
Handoffs 01–04 predate the briefs folder and keep their numbers, which do not line up with the
briefs. The folder has no briefs 03 or 04 (#22's brief was pasted in chat). Handoff 05 was back-filled from the
Design session's comment on #21.
