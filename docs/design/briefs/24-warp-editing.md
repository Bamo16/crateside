# crateside: finishing a warp on Track details

Issue: #105 (and #103, folded in)

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

This brief was written against the commit that adds it. The rules as built are in
`docs/design/DESIGN.md` (*Warp view*, *The player*, and *Playback* under *Differs from the
design*). The latest design of the page is handoff 22 (`docs/design/handoffs/22-playback.md`) and
its canvas *Crateside Playback* in `docs/design/canvases/`; brief 22
(`docs/design/briefs/22-playback.md`) is the first half of this work. The decisions behind this
brief are [ADR 0107](../../adr/0107-a-warp-saved-in-crateside-is-the-maintainers-own.md); bars are
numbered as [ADR 0072](../../adr/0072-bars-are-numbered-as-lives-ruler-numbers-them.md) says. Terms
(starter warp, borrowed warp, default clip, warp marker, bar number) are in `CONTEXT.md`.

---

## Why

Every steady track arrives in staging with a **starter warp**: one marker crateside placed on a
beat, at a tempo it measured, every stem of the upload sharing it. The maintainer then opens the
stems in Ableton Live only to make the last small edits, by ear against Live's metronome:

1. **Move the bar** a beat or two, so the downbeat sits on beat 1 of a bar (the marker's beat is
   right two times in three).
2. Now and then, **nudge** the marker a few milliseconds.
3. Now and then, **halve or double the tempo** (Live's `/2` and `×2`), which Live does one stem at a
   time and gets strangely wrong when the clip does not start on a downbeat.
4. **Set the clip start** to the bar where the first verse begins.
5. **Save the default clip**, which is what says the warp is finished.

Playback (brief 22) is built: the track plays on Track details with a metronome click on the warp's
grid, and the maintainer hears it land on the beat as well as Live's. This brief is the other half:
**those five edits, made in crateside**, so a starter is finished without opening Live.

The model is Live's clip view: edits are a draft the click follows at once, and nothing is final
until *Save*.

## What is decided

### Opening it

- An **`Edit the warp`** opener in the warp view's head line, beside *Judge again* and in the same
  `.outline` small style. It opens an **edit bar** between the head line and the ruler, where the
  judge form opens. The two never show together: opening one closes the other.
- **Offered** whenever the upload's stems share one warp: a starter, a borrowed warp, or one the
  maintainer saved, in staging or in the library, steady or not.
- **Not offered** (the opener keeps its label and says why at `--text-40`, as controls that do not
  apply already do):
  - no stem has a warp (a clip with Warp off, or no sidecar yet): *warp it in Live first*;
  - the stems the track is warped by hold different warps: *the stems hold different warps; settle
    it in Live*.
- The page does not scroll; playback carries on.

### The edit bar

Its controls, in the order the work is done. Every one changes **the draft** only.

- **The bar.** `‹ a beat earlier` and `a beat later ›`. The audio stays where it is and the grid
  moves under it by one beat: every bar line and number shifts, as dragging the one marker by a
  beat does in Live. Readout: `bar 1 starts on the marker's beat 3 · crateside put it on beat 1`
  (the second half only for a starter; beats numbered from 1, as the lane detail already does).
- **The nudge.** `−` and `+` by **1 ms**, **5 ms** with Shift. The marker moves in time and the grid
  with it. Readout: `+4 ms from where crateside put it` (for a warp of the maintainer's: `from the
  saved warp`).
- **The tempo.** `/2` and `×2`, applied to every stem at once, **anchored on the clip start**: the
  clip start keeps its bar number and its moment in the audio, and the bars either side of it
  become half or twice as long. Bar 1 moves, and the marker's beat scales about the clip start
  (landing on a half beat where that is the truth). Readout: `128.00 → 64.00` while it differs
  from what is saved.
- **The clip start.** *Clip start at the playhead's bar*, and while the bar is open the ruler's
  `clip start` notch can be dragged. Both snap to **whole bars**. The clip start is a bar number,
  so moving the grid a beat or nudging keeps it on its bar line; the order the edits are made in
  does not matter. Readout: `clip start · bar 65`. The clip end stays at the file's end and Loop
  stays off, as the starter writes them.
- **Where it goes**, one line under the controls at `--text-60`: `Saves to Instrumental, Drums,
  Bass, Other`, and where a stem is warped on its own, a second: `Vocal keeps its own warp`. No
  per-stem choice anywhere.
- **Save the default clip** (`.primary`), **Revert** (back to what is on disk), **Close**. Close
  with a draft asks first, as leaving the page does (below). No confirm on Save: Undo follows it.

**Keys**, while the bar is open and focus is not in a text field, a slider or the view's window:
`,` / `.` move the bar a beat earlier / later; `←` / `→` nudge 1 ms, Shift 5 ms. (Alt + arrows are
the browser's Back and Forward, so they are not used.)

### The draft is heard and seen at once

Every step redraws as the draft: the ruler's bar numbers, the beat grid, the marker's flag and the
clip-start line on every lane, the tempo line, and the click, which takes the new grid **without
restarting playback**. *Off the grid* is worked out again for the draft on each step, so the dots
can be checked against the ear. The player's position readout counts in the draft's bars. Nothing
is written to disk.

A draft that differs from disk is shown as one: the edit bar says `draft · not saved` and the head
line's gist adds `· draft`. Draw how the lanes show that they hold an unsaved warp; keep it quiet,
since color means state (DESIGN.md) and amber is already *waiting for you*.

### Saving

- **Save writes every stem in *Saves to*** in one go: each sidecar's markers and clip start, the
  saved-clip flag set. Then every stem crateside had lent the warp to is written again from it.
  Each sidecar is backed up first.
- **A saved warp is the maintainer's own**, exactly as one saved in Live: the stems read *warped*,
  their Collection follows from that (it is never set by hand), the track becomes *ready to move*
  once every staged stem is saved, and crateside never replaces it unasked. It is what crateside
  lends to siblings and re-cuts, so a chosen octave is what they get.
- **Done**: a neutral line, `Saved to 4 stems just now · Undo`. **Undo** puts back every stem's
  previous sidecar and re-lends; it lasts until the page is left. After that the backups stay on
  disk only.
- **Live saved one meanwhile**: nothing is written, and the bar says `Not saved: Live saved Drums'
  warp since you started. Revert to load it, then redo the edits.` The draft stays drawn, so its
  numbers can be read off first. A file that could not be written says so in the same place, red,
  `role="alert"`.
- **The lane detail's provenance sentence** for a stem saved here: `Saved in crateside on 17 Nov,
  from crateside's starter: bar a beat later, +4 ms, clip start bar 65.` (and `crateside lends it
  to …` where it does). A later save in Live reads `Saved in Live on …` as today.

### Leaving with a draft

Navigating away or reloading asks *Leave without saving the warp?* (the browser's own dialog on a
reload; the page's on a link). The draft is not kept anywhere.

## Not in this brief

- **Moving the track into the library**: Save makes it *ready to move*; the move stays in Live's
  browser.
- **The clip end**, Loop, adding or removing markers, editing a tempo by typing it.
- **A stem warped on its own** (usually a vocal): never written by this; still done in Live.
- **Several saved warps per song**: nothing here rules them out.
- The chord lane (#198) and mute and solo, as in brief 22.

## What to send back

The head line with the opener (offered, and both refusals); the edit bar at full width and at the
56rem breakpoint, with a starter as it arrives, a draft with each kind of edit made, a warp of the
maintainer's being edited, the *Saves to* line with a stem kept apart; the lanes and ruler showing
a draft, including the clip-start notch being dragged; saving, saved with Undo, *Not saved* after
Live saved, and a failed write; the lane detail's sentence after a save; the leave prompt. Update
the *Crateside Playback* canvas or make a new one, as suits.
