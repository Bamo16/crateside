# crateside design archive

Everything produced in the Claude Design sessions for crateside, as of 2026-09-25 (repo at `7d6f916`).

These are **specs from a point in time**, not the source of truth. The code is. Where the two disagree, the code wins, unless an open issue says otherwise.

## What's here

### `canvases/`: the designs themselves
Open any `.dc.html` in a browser. They need `support.js` beside them. Each is a canvas: turns are stacked **newest at the top**, and every artboard has an id (`1a`, `2b`, …) that the handoffs refer to.

| file | turns | status |
|---|---|---|
| `Crateside Unified Page.dc.html` | 1 · Tracks page · 2 · re-cut and rename · 3 · stem selection, uploads, add track (#22) · 4 · opening and closing rows, keeping your place (4a, #85) | 1–2 built, with divergences recorded in the recut brief. 3: check against code. 4: handoff 13; it removes the panel heading that 3a/3b still draw. |
| `Crateside Track Page.dc.html` | 1 · Track details, all states · 2 · actions, stage flow, embed, genre, activity log (#21) | Built (#21), with differences in `DESIGN.md`. Turn 2 supersedes turn 1 where they differ; the rail's actions and tags are superseded by the Actions Layout canvas. The evidence panel is superseded by Evidence And Sharing (handoff 20). The heading, the band and the upload embed are superseded by Playback (handoff 22). |
| `Crateside Actions Layout.dc.html` | 1 · where a track's actions live, row and Track details (1a–1g, #62) | Built (#62), with differences in `DESIGN.md`. Handoff 06. |
| `Crateside Row Proportions.dc.html` | 1 · the expanded row's two columns (1a–1g) · 2 · genre from the collapsed row (2a–2b, #65) | Handoff 07. Mounts `Crateside Row Stack.dc.html`, so keep the two together. |
| `Crateside Row Stack.dc.html` | the side stack as a reusable piece, mounted by Row Proportions | Handoff 07. |
| `Crateside Counts Page.dc.html` | 1a · the Library page as **Counts**, whole page · 1b · a detector band opened | Handoff 08 (#68, #60, #56). |
| `Crateside Tools Page.dc.html` | 1a · Partners inside **Tools** · 1b–1d · Re-Pitch · 2a · Re-Pitch inside Partners' table (not recommended) | Handoff 09 (#57, #58). |
| `Crateside Settings Page.dc.html` | 1a · Settings, everyday · 1b · first run · 1c · removal confirm and failures | Handoff 10 (#74). |
| `Crateside Dashboard.dc.html` | 1a · Counts becomes the Dashboard, whole page, sections folding (#78) | Handoff 11, built with differences in `DESIGN.md`. *Latest activity* in its *Over time* row is superseded by Activity Panel (handoff 21). |
| `Crateside BPM Display.dc.html` | 1 · how each tempo state reads, everywhere a BPM shows (1a–1f) · 2 · how much a drifting tempo drifts: range, spread, tempo strip (2a–2c) | Handoff 13 (#92). |
| `Crateside Warp View.dc.html` | 1 · each stem's warp in Track details: two markers (1a), dense with a stem on its own (1b), 8-bar zoom (1c), every provenance state (1d), both controls step by step (1e), zoom and room to grow (1f) | Handoff 14 (#97). The zoom pills and the zoomed-only overview strip (1c, 1f) are superseded by Playback (handoff 22). |
| `Crateside Off Its Tempo.dc.html` | 1 · a warp that sits off its tempo: the BPM-cell mark and filter (1a), Track details (1b), the Off the grid row and True up step by step (1c), the shape of each state (1d) | Handoff 16 (#96). Delivered as a bundle (v2), then as a `.dc.html` that needs `support.js` (v3, which also draws the warp view's bar numbers and time axis). |
| `Crateside Mode Beside Its Tag.dc.html` | 1 · a verdict's mode beside its tag: the Tracks Key cell (1a), the Track details band (1b), Why this key with sections (1c), Partners (1d, a proposal, superseded by handoff 19) | Handoff 17 (#114). |
| `Crateside Judge From These Bars.dc.html` | 1 · judging from bars the maintainer names: the band (1a), the warp view form, bands and run states (1b), Why this key's windows (1c), the Ear setting (1d) | Handoff 18 (#116). 1c (window blocks in Why this key) superseded by handoff 20, where windows open from their bands. |
| `Crateside Partners By Notes.dc.html` | 1a · Partners pairs by notes: notes shared, the shift span, sections as a basis, tonic and mode by hand; live | Handoff 19 (#125). Supersedes 1d of Mode Beside Its Tag. |
| `Crateside Evidence And Sharing.dc.html` | 1 · Track details: the evidence panel in two balanced columns (1a), the warp view as the track's timeline, with window drawers, the key strip and room for chords and playback (1b), the share card (1c); live | Handoff 20 (#150, #151, #152). The transport in the ruler's gutter (1b) is superseded by Playback (handoff 22); its playhead stays. |
| `Crateside Activity Panel.dc.html` | 1 · the dashboard's Activity section: in place at 2000px, live, with filters, paging and opened rows (1a), every row state (1b), narrow, paged away, empty and folded (1c) | Handoff 21 (#189). Supersedes *Latest activity* in the Dashboard canvas's *Over time* row. |
| `Crateside Playback.dc.html` | 1 · Track details with a header strip and a player: full width (1a), pinned (1b), at 56rem (1c) · 2 · the player's states, the header's variants, the upload card without its embed (2a) · 3 · the warp view with an always-on window: live (3a), every state of the window (3b), seeking (3c) | Handoff 22 (#105). The Design artifact keeps one file per artboard; here they are stacked in one file. |
| `Crateside Brand.dc.html` | 1 · five marks · 2 · fifteen more | **1a chosen** (interim). The rest are kept in case the brand is revisited. |

### `handoffs/`: what was sent to the implementing agent each time
The written spec for each canvas turn, in the order it was sent. The later ones assume the earlier ones are built.

| handoff | canvas | issue |
|---|---|---|
| `01-tracks-page.md` | Unified Page, turn 1 | |
| `02-recut-and-rename.md` | Unified Page, turn 2 | |
| `03-stem-selection-issue-22.md` | Unified Page, turn 3 | #22 |
| `04-brand.md` | Brand | |
| `05-song-page-issue-21.md` | Track Page | #21 |
| `06-actions-layout.md` | Actions Layout | #62 |
| `06b-workflow-fixes.md` | none: a review of this workflow; the round trip is now ADR 0057's | |
| `06c-link-delivery.md` | none: results delivered by link, now only a fallback (ADR 0057) | |
| `07-row-proportions.md` | Row Proportions, Row Stack | #65 |
| `08-library-as-reports.md` | Counts Page | #68, #60, #56 |
| `09-mashup-tools.md` | Tools Page | #57, #58 |
| `10-settings.md` | Settings Page | #74 |
| `11-dashboard.md` | Dashboard | #78 |
| `12-tools-together.md` | Tools Together | #79 |
| `13-bpm-display.md` | BPM Display | #92 |
| `13-row-motion.md` | Unified Page, turn 4 | #85 |
| `13b-picker-layout-fix.md` | Unified Page, turn 4 (corrects the build of 03 and 13) | #85 |
| `14-warp-view.md` | Warp View | #97 |
| `16-off-its-tempo.md` | Off Its Tempo | #96 |
| `17-mode-beside-its-tag.md` | Mode Beside Its Tag | #114 |
| `18-judge-from-these-bars.md` | Judge From These Bars | #116 |
| `19-partners-by-notes.md` | Partners By Notes | #125 |
| `20-track-page.md` | Evidence And Sharing | #150, #151, #152 |
| `21-activity.md` | Activity Panel | #189 |
| `22-playback.md` | Playback | #105 |

### `brand/`: the 1a assets
`mark.svg` (header), `favicon.svg` plus PNG fallbacks (browser tab), `lockup.svg` (README), `mark-tile.svg` (app icon). Usage rules are in `handoffs/04-brand.md`.

### `briefs/`: what the design sessions were asked for
The briefs written by the implementing agent. #22's brief was pasted in chat and isn't included; it's in the issue.

## Design language, in one place
The live definition is `src/Crateside/wwwroot/app.css`. The rules the canvases follow:
- One warm dark palette: `--bg #141310`, `--surface #1b1a15`, `--raised #23211a`, `--text #ece7dc`, with alpha tiers for prose (75/60/50) and for labels only (40/35/30).
- Color means something, and nothing else is colored. **Teal** = machine working, **amber** = your turn, **red** = needs attention, **green** = done, only ever done.
- IBM Plex Sans for text; IBM Plex Mono for every number, key, timestamp and count.
- State chips: neutral outline · teal filled with a dot · teal outline · amber filled · red outline · red filled · green outline · dashed.
- Three page-header shapes (Bar, Flush, Shade), and a closed set of them.
- One width cap per table shape (`--table-*`).

## Numbering (added 2026-09-25)

From brief 05 on, a handoff takes its brief's number: `briefs/NN-<topic>.md` → `handoffs/NN-<topic>.md`.
Handoffs 01–04 predate the briefs folder and keep their numbers, which do not line up with the
briefs. The folder has no briefs 03 or 04 (#22's brief was pasted in chat), and no brief for the row-motion work (given to the Design session directly), whose handoffs `13-row-motion.md` and `13b-picker-layout-fix.md` took brief 13's number by mistake; `13-bpm-display.md` is the handoff for brief 13. Handoff 05 was back-filled from the
Design session's comment on #21.

Handoffs outside the brief sequence take a letter after the brief they follow: `06b-workflow-fixes.md`
(the Design session's review of this workflow) and `06c-link-delivery.md` (results delivered by link).
