# Handoff: crateside unified Tracks page

## Overview
Replaces the Queue page, Staging board, and Audit page with **one page**: a frozen activity header, one filterable list, one row per track. Answers the friction list in `uploads/crateside-ui-restructure-scoping.md` (also copied here as `scoping.md`): rows never move between sections, held tracks expose the instrumental control, running work is visible at a glance, and a toast fires when a track is ready to warp. `Partners` stays a separate page.

## About the design files
`Crateside Unified Page.dc.html` is a **design reference built in HTML**, not code to ship. Recreate it in the existing Blazor Server app (`src/Crateside/Components/Pages/`) using its patterns. The file opens in a browser; it is a canvas of artboards labeled 1a–1i. **Implement 1a–1d.** 1e–1i are rejected alternatives — ignore them.

Vocabulary in the design came from the scoping doc only. Where it conflicts with `CONTEXT.md` (crateside or keytag), **CONTEXT.md wins** — fix the label, keep the layout.

## Fidelity
**High-fidelity.** Colors, type, spacing and copy are final. Match them.

## Screens

### 1a — Tracks page (default, "All" filter)
Page width 1280 target; desktop only, often a second monitor. Background `#141310`.

**Frozen header** — `position: sticky; top: 0`, background `#1b1a15`, bottom border `rgba(255,255,255,.08)`. Grid `200px 1fr 1fr 300px`, cells divided by 1px `rgba(255,255,255,.08)` right borders.
1. Brand + nav (padding 18px 22px, column, space-between): "crateside" 15px/600; nav row gap 14px, 12px/500: active tab `#ece7dc` with 2px `#e8b45a` bottom border, inactive `rgba(236,231,220,.5)`. Tabs: Tracks, Partners.
2. Separation (padding 16px 22px, column, gap 8px): label row — "SEPARATION" 11px/500 uppercase, letter-spacing .08em, `rgba(236,231,220,.5)`; right side "2 waiting" in mono. Activity line — 8px teal dot, track name 16px/500 (ellipsis), elapsed `4:12 elapsed` mono 13px `rgba(236,231,220,.6)`. Below: 3px track `rgba(255,255,255,.08)` with an **indeterminate** sweep (30% wide gradient `transparent → #6fc7d6 → transparent`, translateX −40%→140%, 2.4s linear infinite). Leave this bar in place — it becomes a real progress bar when StemForge#84 lands. Idle state: dot gray, text "idle", bar empty.
3. Judging: same structure; activity line adds elapsed mono and a **Pause** button pushed right (12px/500, transparent, 1px `rgba(255,255,255,.18)` border, radius 6, padding 5px 10px; hover border `.4`). Bar is determinate here only because judging is ~1 min — 68% shown; indeterminate is acceptable. When paused: label "Resume", dot amber.
4. Claude quota: label "CLAUDE QUOTA" with "resets 6:40 pm" mono at right; "43%" mono 22px/600 + "of this 5-hour window" 13px `rgba(236,231,220,.6)`; 3px bar fill `#ece7dc` at percentage.

**Filter bar** — flex, gap 6px, padding 14px 22px, bottom border `rgba(255,255,255,.06)`. Pills: 12px/500, padding 6px 12px, radius 999. Inactive: text `rgba(236,231,220,.75)`, 1px border `rgba(255,255,255,.1)`. Active: filled — All `#ece7dc`, In staging `#e8b45a`, Needs attention `#e07a6d`, others `#ece7dc`; text `#0f0e0c`. Count in mono; count color when inactive: In staging amber, In flight teal `#6fc7d6`, Needs attention red, others 60% opacity. Right-aligned status text 12px `rgba(236,231,220,.45)`: "433 samples in Mashup Samples · playlist synced 2 min ago".

Filters and membership:
- **All**
- **In staging** — state `staged & tagged` (ready to warp)
- **In flight** — detecting, judging, judged (waiting for GPU), separating, staging, tagging, queued
- **Needs attention** — parked (LOW/WITHHELD), gave up, any row with an audit flag
- **Held**
- **Done** — in Mashup Samples

**Column heads** — grid `24px 1fr 90px 100px 80px 190px 1fr`, gap 14px, padding 8px 22px, 10.5px/500 uppercase .08em `rgba(236,231,220,.4)`: (chevron) Song · Key · Confidence · BPM (right-aligned) · State · Next.

**Row** (collapsed) — same grid, height 46px, padding 0 22px, top border `rgba(255,255,255,.06)`, hover bg `rgba(255,255,255,.025)`, whole row clickable to expand.
- Chevron `›` (expanded `⌄`) mono 12px `rgba(236,231,220,.35)`.
- Song 14px/500 ellipsis. Optional audit flag after it: 10.5px/500 red `#e07a6d`, 1px `rgba(224,122,109,.5)` border, radius 4, padding 1px 6px, e.g. "tag disagrees".
- Key mono 13px/500 `#ece7dc`; `—` at `rgba(236,231,220,.3)` when no confident key. Use ♭/♯ glyphs.
- Confidence mono 11.5px letter-spacing .04em: HIGH `rgba(236,231,220,.6)`, MEDIUM `#ece7dc`, LOW/WITHHELD `#e07a6d`, none `—` dim.
- **BPM** mono 14px/600 right-aligned. `#e8b45a` when the track is ready to warp, `#ece7dc` otherwise, `—` dim when unknown.
- State chip: 12px/500, padding 4px 10px, radius 999, 1px border, optional 6px dot. See tokens → chips.
- Next 13px: amber when it's the user's turn, `#ece7dc` when needs attention, `rgba(236,231,220,.6)` otherwise. Derived, plain English (see copy table).

**Row expanded** — panel padding 6px 22px 18px 60px, grid `1fr 300px`, gap 32px.
Left: heading "STEMS · SAMPLE STAGING / {song}" (10.5px uppercase .4). One line per stem — grid `1fr 110px 110px 1fr`, gap 14px, padding 8px 12px, radius 6, margin-bottom 4px:
- Kept stem: bg `rgba(255,255,255,.03)`, 1px solid `rgba(255,255,255,.12)`. Label 13px/500 (`Vocal - Full`, `Instrumental - Full`). Tag column mono 12px: `F min ✓` green `#7fcf9a`, `not tagged` red, otherwise `.6`. Warp column mono: `warped ✓` green / `not yet` `.6`. Fourth column italic 12px `.5` for audit note.
- **Warp guide (Drums)**: same grid but bg transparent, 1px **dashed** `rgba(255,255,255,.12)`, whole line opacity .55, label suffixed with mono 11px "warp guide", tag cell "never tagged", note "for placing markers; deleted after warping". It must read as not-a-sample.
Right (padding-top 22px): detail sentence 12.5px/1.5 `rgba(236,231,220,.7)` (verdict, agreement, staged-since), then action buttons (same outline button style): Open folder · Re-judge · Hold.

**Toast** — fixed bottom-right 22px, width 360, bg `#23211a`, 1px `rgba(232,180,90,.45)`, radius 10, padding 14px 16px, shadow `0 16px 40px rgba(0,0,0,.6)`. Grid `1fr auto`: eyebrow "READY TO WARP" 10.5px uppercase amber; song 15px/500; `D min · 128` mono 15px/600 right; footer 12px `.55` "3 stems in Sample Staging, key-tagged." + underlined "Show row" link (scrolls to and expands the row). Fires once per track when it reaches staged & tagged. Auto-dismiss ~20s; stack newest on top.

### 1b — In staging filter
Same page, "In staging" pill active (amber). Rows height 52px, song 15px, key mono 14px, **BPM mono 16px/600 amber**. Last column becomes **Since** ("staged 3 weeks ago") instead of Next. Status text right of pills: "Staged and tagged. Drag from Sample Staging; move to Mashup Samples when warped." Footer line 12.5px `.4` under the list: "Next to arrive: {track} — separating, ~2 min left, then staging and tagging." (omit when nothing is separating).

### 1c — Needs attention filter
Pill active red. Last column header "What is wrong": problem text 13px `#ece7dc` + a right-aligned outline button with the single fix verb (Retag / Set key / Retry). Rows from `TagAudit` findings + parked + gave up.

### 1d — Held track, expanded
Chip "held": text `.7`, 1px **dashed** `rgba(255,255,255,.3)`. Key/Conf/BPM show `—` dim if unknown. Next: "nothing will touch it until you release it".
Expanded left heading "WILL SEPARATE INTO", listing the planned recipe: skipped stems struck-through at opacity .45 with italic reason ("instrumental source — no vocal to lift"); pending stems "not yet"; Drums warp guide as above. Below: toggle (34×20, radius 10, on = `#e8b45a` with `#0f0e0c` knob) "Instrumental source" + hint "changeable until separation starts · preset picker lands here later". Toggle is enabled for any track before separation begins (held included) and disabled after. Right: detail sentence + primary button **Release hold** (bg `#ece7dc`, text `#0f0e0c`, radius 6, padding 7px 12px) and outline **Remove from playlist**.

## Copy — state chip and Next column

| internal state | chip | style | Next |
|---|---|---|---|
| queued | queued | neutral outline | downloading next |
| detecting | detecting key | teal filled + dot | then judging |
| detected / judging | judging | teal filled + dot | verdict in about a minute |
| judged, waiting GPU | judged · in line | teal outline | next on the GPU / 2nd for the GPU · ~8 min |
| separating | separating | teal filled + dot | ~N min left, then staging |
| separated / staging | staging | teal filled + dot | moving stems into Sample Staging |
| tagging | tagging | teal filled + dot | writing Ableton key tags |
| staged & tagged | ready to warp | amber filled | your turn — warp, then move to Mashup Samples |
| parked (LOW/WITHHELD) | parked · no key | red outline | needs a better verdict — nothing automatic will move it |
| gave up | gave up · {stage} ×3 | red filled | retry, or hold it |
| held | held | dashed outline | nothing will touch it until you release it |
| done | done | green outline | in Mashup Samples |

Chip styles — teal filled: bg `rgba(111,199,214,.14)`, text/dot `#6fc7d6`, border `rgba(111,199,214,.3)`. Teal outline: same without bg. Amber filled: bg `rgba(232,180,90,.16)`, text `#e8b45a`, border `rgba(232,180,90,.35)`. Red outline: text `#e07a6d`, border `rgba(224,122,109,.45)`; red filled adds bg `rgba(224,122,109,.18)`. Green outline: text `#7fcf9a`, border `rgba(127,207,154,.3)`. Neutral: text `rgba(236,231,220,.7)`, border `rgba(255,255,255,.14)`.

Color rule: **teal = machine working, amber = user's turn, red = needs attention.** Nothing else is colored.

## Interactions & behavior
- Click row → expand/collapse (single expanded at a time is fine). Chevron rotates; no animation required beyond 150ms ease on the panel.
- Filter pills → filter the one list client-side; counts always reflect the full list.
- Pause/Resume judging in header (existing `DrainControl`).
- Toast on staged & tagged; "Show row" switches to All (or In staging), scrolls the row into view without `scrollIntoView` side-effects on the page, expands it.
- Header sweep animation runs only while separating.
- Row-level updates from a single change notification (see constraints); rows must not reorder on state change. Default sort: playlist order.

## State & data
- Per-row: `StagingSong`/`StagingStem` fields (name, key, confidence, BPM, stems with tag + warp status) + `QueueItem` state + `TagAudit` findings + held flag + instrumental flag + timestamps (staged-since, held-since).
- Header: current separation item + elapsed + queue depth; current judging item + elapsed + depth + paused; quota % + reset time.
- Constraints from the scoping doc apply: one notification source for stage transitions; cached `QueueService.ReadAsync()` with debounced refresh; individual row updates.

## Design tokens
Fonts: **IBM Plex Sans** (text) and **IBM Plex Mono** (every number, key, timestamp, count) — Google Fonts or self-host.
Colors: page `#141310`, header `#1b1a15`, toast `#23211a`, canvas `#0f0e0c`; text `#ece7dc`; muted text at `.75/.6/.5/.45/.4/.35/.3` alpha of `rgb(236,231,220)`; dividers `rgba(255,255,255,.06)` rows, `.08` header; teal `#6fc7d6`; amber `#e8b45a`; red `#e07a6d`; green `#7fcf9a`.
Type: 22/16/15/14/13/12.5/12/11.5/11/10.5 px; weights 400/500/600; uppercase labels 10.5–11px, letter-spacing .08em.
Radius: pills 999, buttons/stem lines 6, toast 10, flags 4. Row height 46 (52 in staging). Grid gap 14. Page gutter 22.

## Assets
None. No icons — text and dots only.

## Files
- `Crateside Unified Page.dc.html` — the design canvas (1a–1d implement; 1e–1i ignore). Opens in any browser.
- `scoping.md` — the original scoping document this design answers.
