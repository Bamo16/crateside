# Handoff: re-cut stems and renaming — crateside Tracks page

## Overview
Two additions to the built Tracks page, answering `brief.md`. Nothing on the page moves; both capabilities live inside the existing expanded panel and reuse the chip/flag vocabulary. No new colors.

## About the design file
`Crateside Unified Page.dc.html` opens in any browser. It is a **design reference**, not code to ship. Turn **2** (top of the canvas, artboards **2a–2d**) is this spec. Turn 1 below it is the earlier page design — already built, already diverged in the six ways `brief.md` lists; do not re-apply it.

Tokens, chip styles, grids and vocabulary are those in `brief.md` ("Design tokens" and "The current page, exactly as built"). Where the drawing and the brief disagree on a token, the brief wins.

## Fidelity
High. Copy in the artboards is final unless marked as a question in 2d.

---

## 2a — Re-cut stems (panel body: STEMS · SAMPLE STAGING)

### The Release control
Below the stem lines, `margin-top: 16px`, flex row, gap 12:
- Label `RELEASE` — 10.5px/500 uppercase .08em text-40.
- **Select-shaped control** — outline button style (12px/500, 1px `rgba(255,255,255,.18)`, radius 6, padding 6px 11px) with a `▾` at text-50. Values today: `no vocals` / `has vocals`. This slot is where the preset picker lands later: same box, a menu instead of two values.
- Hint text-50: "the stems follow from this · changing it re-cuts".

Use the same control in the **before-separation** body in place of the toggle, so the two bodies match. (The toggle from the earlier pass is superseded.)

Rule: the recipe is derived from the fact; the user never picks stems.

### Confirm state (choosing a different value)
Nothing runs until confirmed. The panel re-renders:
- Heading gains ` · re-cut pending` in red `#e07a6d`.
- Stem lines become a **diff**:
  - Existing files: 1px `rgba(224,122,109,.35)` border (dashed for the warp guide), opacity .6/.5, name and key struck through, *Where it is* cell reads `removed` in red, note italic text-60 ("deleted from Sample Staging" / "re-made below").
  - New files: bg `rgba(111,199,214,.06)`, 1px `rgba(111,199,214,.3)` (dashed + opacity .7 for the warp guide), *Ableton key* cell shows the existing verdict at text-60, *Where it is* reads `new` in teal, first new line notes "tagged from the existing verdict".
- Release control shows the new value with border `.4`; beside it "was **no vocals**" (text-50 / text-75).
- Right column (340px) replaces the button row:
  - Red sentence 13px: "Re-cutting removes N files from Sample Staging."
  - Detail 12.5px/1.5 text-75: "Anything you've done to them in Ableton is lost. The key stays E major — detection does not run again, so no quota is spent. The downloaded audio is still on disk; nothing is re-downloaded. Takes about 4 minutes on the GPU."
  - Buttons: destructive **Re-cut · remove N files** (bg `rgba(224,122,109,.18)`, text `#e07a6d`, border `rgba(224,122,109,.45)`) and outline **Keep as is**.
- Cancel/Keep restores the control to the recorded value.

### Locked state
Files in Mashup Samples: control is inert — text-50, 1px **dashed** `rgba(255,255,255,.18)`, no `▾`. Hint text-60: "N files are in Mashup Samples. Move them back to Sample Staging to change this."

### Behavior
- Preconditions: all staged files present in Sample Staging (guarantees original audio on disk).
- On confirm: record the new fact, delete the listed files, re-run separation → staging → tagging using the **existing verdict** (key detection must not run).
- Ready-to-warp toast fires again when re-staged.

---

## 2b — Renaming (name strip)

A strip at the top of **every** expanded panel, spanning both columns: padding `6px 22px 12px 60px`, bottom border `rgba(255,255,255,.06)`, `margin-bottom: 6px`, flex baseline, gap 12.

### Derived (default)
- Name 15px/500 `#ece7dc`.
- Provenance text-50: "from the ledger" / "from the staging board" / "from the playlist title" — whichever source won.
- Right-aligned outline **Rename**.

### Editing
Strip becomes a `1fr 340px` grid (gap 32):
- Left: `<input>` pre-filled with the current name — 15px/500, bg `rgba(255,255,255,.04)`, 1px `rgba(255,255,255,.4)`, radius 6, padding 8px 12px, full width. Below, text-50: "Derived **{derived name}** · from {source}. Your name wins over ledger, staging board and playlist."
- Right: detail sentence (12.5px/1.5 text-75) describing what happens for this track's state, then primary **Rename · {scope}** + outline **Cancel**.
- Button copy by state:
  - nothing downloaded: `Rename · nothing on disk yet`
  - separating: `Rename · 1 file now, 3 at staging` (see question 5)
  - files in staging: `Rename · 3 files + folder info`
  - in Mashup Samples: control locked; hint "rename in Ableton"
- Enter saves, Esc cancels. Empty input = disabled button.

### Overridden
- Collapsed row: after the name, a **neutral flag** — 10.5px/500 text-60, 1px `rgba(255,255,255,.2)`, radius 4, padding 1px 6px — reading `renamed`. Same slot and shape as audit flags.
- Strip: name; text-50 "renamed by you · was **{derived}**"; right-aligned outline **Revert** and **Rename**.
- Revert drops the override and renames files back to the derived name (same scope rules).

### Behavior
- The override is stored separately from the three derived sources and always wins (see question 2).
- Rename touches every file named `<track name> (<preset>)`, the Ableton Folder Info XMP entry, and — before separation — the source audio's tags.

---

## 2c — In progress, collapsed row

**Re-cut** re-enters the pipeline, so it uses the existing chips with a suffix: `separating · re-cut`, `staging · re-cut`, `tagging · re-cut` — teal filled + dot. Row appears under *In flight*, returns to `ready to warp` when re-staged. No new chip.

**Rename** does not change pipeline state, so the chip is untouched. A flag in the name cell:
- running: teal — text `#6fc7d6`, border `rgba(111,199,214,.3)`, 5px teal dot, `renaming 3 files`
- failed part-way (e.g. file open in Ableton): red audit-style flag `rename failed · 1 of 3`; row joins *Needs attention* with a **Retry** fix button; expanded strip explains which file.
- Success: flag becomes the neutral `renamed`.

---

## 2d — Open questions (decide before or during build)
1. Is a re-cut a **new fact** or a **correction**? Drawn with no history ("was no vocals"). Affects the ledger and whether the detail sentence can say "re-cut 2 days ago".
2. Does a rename override **survive re-judging**? Drawn: yes, always. Alternative: strip offers "judging suggests X · Use it".
3. Preset picker — names and shape. Drawn as the same select slot; confirm the label "Release" still reads truthfully.
4. Re-cut in the other direction (vocal split → drums-only): allowed? Diff drawn identically, removed count 3.
5. Rename while separating: does the running separator pick up new source tags mid-job, or are stems renamed at staging? Button copy assumes the latter.
6. `renamed` flag: permanent (drawn) or dropped at *done*?
7. Ableton has the file open: drawn as fail-after with Retry; a pre-check is better if the app can know.

## Files
- `Crateside Unified Page.dc.html` + `support.js` — design canvas; turn 2 (2a–2d) is this spec.
- `brief.md` — the design brief this answers; source of truth for tokens and vocabulary.
