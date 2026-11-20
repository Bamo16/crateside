# Handoff 24: finishing a warp on Track details

Brief 24, issue #105 (with #103 folded in), ADR 0107 · built against `42656f8`
Canvas: `Crateside Warp Editing.dc.html`. **1a** is Track details at full width with the edit bar
open on a starter as it arrives. **1b** is the warp view's head line: offered, both refusals, and
with each of the edit bar and the judge form open. **2a** is the edit bar at full width, numbered:
as it arrives, each kind of edit, the maintainer's own warp, Close's question, the *Saves to*
variants. **2b** is the edit bar at 56rem. **3a** is the ruler and lanes holding a draft, zoomed
and whole, the clip-start notch being dragged. **4a** is saving, saved with Undo, undone, *Not
saved* after Live saved, a failed write and the lane detail's sentence. **4b** is the leave prompt.

The track drawn is invented: a steady 128 BPM song of 5:00, four stems (Instrumental, Drums, Bass,
Other) sharing crateside's starter, and a Vocal warped on its own. The waveforms are drawn shapes;
Drums is drawn as beat ticks (short on every beat, tall on each true downbeat) so the grid can be
seen landing. Bracketed text is a placeholder. The header strip, player, stage flow and the rest of
the warp view are handoff 22's and DESIGN.md's, unchanged.

**The rule:** the edit bar is a draft editor, as Live's clip view is. Everything it touches redraws
at once in the browser and nothing is written until *Save the default clip*. Nothing new is colored:
the draft is shown in `--text` and outline, never amber; teal only while crateside writes; red only
for a write that failed.

## 1. Opening it (1b)

- **`Edit the warp`**: `.outline` small, in the head line right after *Judge again*.
- **Offered** whenever the stems the track is warped by share one warp: starter, borrowed or the
  maintainer's, staging or library, steady or not.
- **Not offered**: the button is disabled and keeps its label, the reason after it at 12
  `--text-40`:
  - no stem has a warp: `warp it in Live first`;
  - the authority stems hold different warps: `the stems hold different warps; settle it in Live`.
    The gist then reads `4 stems · two warps · Instrumental and Drums differ`, the `two warps` at
    `--text` as *warped on its own* already is.
- **One at a time.** Whichever of the edit bar and the judge form is open hides its own opener and
  keeps the other's (1b·4, 1b·5). Clicking the other's opener closes the open one; with a draft,
  Close's question (2a·7) comes first.
- The page does not scroll; playback carries on.

## 2. The edit bar (1a, 2a)

Between the head line and the View row, where the judge form opens, 14 px above the View row:
`--surface`, 1px `--text` 12%, radius 8, as the judge form. `role="region"`,
`aria-label="Edit the warp"`. Three parts.

**The top line** (padding 11px 16px 0, 12.5 `--text-60`): the caps label `EDIT THE WARP` at
`--text` (not amber, unlike the judge form's: amber would read as the lanes waiting on the
maintainer), then what is being edited in the lane chips' words, `crateside's starter · 128.00 · 1
marker at 0:00.52` (for the maintainer's, the filled `Your warp` chip and `saved in Live 2 Nov ·
100.00 · …`; for a borrowed one, `borrowed from …` as the lane says it). At the right:

- no draft: `as on disk`, mono 12 `--text-40`;
- a draft: `○ draft · not saved`, mono 12/500 `--text`, the ring a 7 px circle with a 1.5 px
  stroke.

**The cells**: four equal columns under a `--line` rule, `--line` dividers between them, each
padding 10px 16px 12px: the caps label, the controls (small buttons, gap 6), then the readout in
mono 12. **A readout that differs from disk is at `--text`; one that does not is at `--text-50`**,
so a glance across the bar shows what the draft changed.

| cell | controls | readout, as on disk | readout, a draft |
|---|---|---|---|
| **Bar** | `‹ a beat earlier` · `a beat later ›` | `bar 1 starts on the marker's beat 1, where crateside put it` (maintainer's: `…, as saved`) | `bar 1 starts on the marker's beat 3 · crateside put it on beat 1` |
| **Nudge** | `−` `+` (30 px square, mono), then `1 ms · Shift 5 ms` at 11.5 `--text-40` | `on crateside's marker` (maintainer's: `as saved`) | `+4 ms from where crateside put it` (maintainer's: `−2 ms from the saved warp`) |
| **Tempo** | `/2` `×2` (mono), then `every stem, about the clip start` | `128.00` | `128.00 → 64.00`, the new figure at 600 |
| **Clip start** | `Clip start at the playhead's bar` | `clip start · bar 1 · or drag its notch on the ruler` (the hint in the body face at `--text-40`) | `clip start · bar 65 · was bar 1` |

- **The bar readout's second form.** Once `/2` or `×2` puts bar 1 outside the file (or more than a
  bar from the marker), *bar 1 starts on the marker's beat N* stops meaning anything, so it reads
  where the marker falls instead: `the marker falls on bar 32, beat 4 · crateside put it on bar 1,
  beat 1` (2a·5). A half beat reads `beat 2½`.
- **Clip start at the playhead's bar** takes the bar the playhead is in (`bar 65.2.3` → 65), never
  the next.

**The foot** (padding 10px 16px 12px, a `--line` rule above): *Where it goes* at the left, 12.5
`--text-60`, one line or two; then the key hint at 11.5 `--text-40` with key caps (`,` `.` a beat ·
`←` `→` 1 ms); then the buttons: **Save the default clip** (`.primary`), **Revert**, **Close**.

- *Saves to* (2a·8): `Saves to Instrumental, Drums, Bass, Other`, and a second line only where a
  stem keeps its own warp (`Vocal keeps its own warp`; two: `Vocal and Backing Vocal keep their
  own warps`). Where a save will lend the warp on, the second line says so in the same voice:
  `then lends it again to Piano` (2a·6).
- **Save is live with no draft on a starter or a borrowed warp**: saving one as it is accepts it,
  and that is the finished warp the brief's fifth step makes. On the maintainer's own warp with no
  draft it is disabled and titled `nothing to change`. **Revert** is disabled with no draft.
- **Close with a draft** (2a·7) uses the house confirm in the foot: the buttons give way to `Close
  without saving the warp?` (amber, 12.5/500), **Close without saving** (`.danger`) and **Keep
  editing**; the cells' controls are disabled while it asks. Esc is *Keep editing*. *Judge again*
  with a draft asks the same before opening the form. Close with no draft just closes.

**Keys**, while the bar is open and focus is not in a text field, a slider (the clip-start notch
included) or the view's window: `,` / `.` the bar a beat earlier / later; `←` / `→` nudge 1 ms,
Shift 5 ms.

### At 56rem (2b)

The cells go two by two (Bar, Nudge; Tempo, Clip start), the second row under a `--line` rule. The
foot wraps: *Saves to* takes the full width, the three buttons follow right-aligned, and the key
hint is dropped. The head line is as at full width.

## 3. The draft, heard and seen (3a)

Every step redraws as the draft, in the browser: the ruler's numbers and ticks, the beat grid, the
tempo line and its gutter figure, *Off the grid* (worked out again: 3a·1 to 3a·3 shows its dots
going from 4 ms early to the zero line and the gutter dropping `· 4 ms early`), each saving lane's
marker flag and clip-start line, and the click, which takes the new grid without restarting. The
player's position counts in the draft's bars.

**The view keeps its place in the audio** while the grid moves under it (the audio stays, the grid
moves). After a bar move it therefore sits off the bar lines (3a·2), as after a pan, until the next
zoom, handle or `‹ ›` lands it on bars again. The readout gives the bars it spans, rounded as after a
pan.

**How the lanes show an unsaved warp**, quietly and without color (3a·2 to 3a·6):

- each saving lane's gutter adds `○ draft` at `--text` after its chip (the chip keeps saying what is
  on disk: `Starter` dashed, until the save);
- its clip-start line is drawn **dashed**, 1px `--text` 75%, where it is solid `--text-50` on disk;
- its marker flag is drawn **hollow** (the 7×9 pennant outlined in `--text` 85% on `--bg`), filled
  again once saved;
- a stem the save does not touch (the Vocal on its own warp) is drawn exactly as before.

The head line's gist ends `· ○ draft` at `--text` (1b·4).

**The clip-start notch** (3a·3, 3a·4). While the bar is open the ruler's `clip start` notch is a
grip: an 11×16 downward pennant at `--text` 75%, on the bar line, at the ruler's foot, label beside
it as built. Hovered and held, full `--text`; `ew-resize`. It moves bar line by bar line (whole
bars only); while held a tag over it names the bar (`clip start · bar 64`, the window tag's style),
a dashed 11×16 ghost keeps the place it left, and the clip-start line runs **solid** 1px `--text`
down the saving lanes. Release sets the draft; Esc or `pointercancel` puts it back. It is
`role="slider"` (`aria-valuetext="bar 65"`): ←/→ a bar, Shift four. With focus on it the arrows move
the notch and do not nudge. While the bar is closed the notch is the plain tick and label it is
today.

**`/2` about bar 65** (3a·5): bar 65 keeps its place and number; 63, 64 and 66 are twice as wide;
the beat grid's lines fall on every other beat of the old grid; the tempo gutter reads `64.00 was
128.00`.

## 4. Saving (4a)

- **Saving** (4a·1): the edit bar folds into one line in the judge form's running shape: a
  teal-bordered `--surface` line, a 7 px teal dot, `Saving to 4 stems · backing each sidecar up,
  then writing`, the count `2 of 4` right at 12 `--text-50`, `role="status"`. Each saving lane's chip
  becomes a teal outline `writing…`. The head line shows no opener while it runs. Leaving does not
  ask: no draft is left to lose, and the write finishes either way.
- **Saved** (4a·2): the line turns neutral (`--surface`, 1px `--text` 12%): `Saved to 4 stems just
  now · Undo` (Undo underlined, a button). The opener returns. The lanes read `Your warp` filled,
  `saved in crateside 17 Nov`. The stage chip moves to *ready to move* once every staged stem is
  saved. The line, and Undo with it, stays until the page is left or the bar is opened again.
- **Undo** writes too, so it runs as Saving does (`● Putting 4 stems back…`), then reads `Put back:
  the 4 stems are as they were before the save` (4a·3). The lanes are the starter again. Undo is not
  itself undone; *Edit the warp* starts over.
- **Not saved: Live saved one meanwhile** (4a·4): nothing written; the bar stays open with the
  draft's readouts so they can be read off. A line sits between the cells and the foot: **Not
  saved:** in amber 600, then `Live saved Drums' warp since you started. Revert to load it, then redo
  the edits.` at `--text`. *Save* is disabled until *Revert* is used; the edit controls stay live.
- **A failed write** (4a·5): the same place, red text on a 6% red ground with a red rule above,
  `role="alert"`: `Couldn't save: Bass - Full.wav.asd could not be written ([reason]). Nothing
  changed; every stem is as it was.` True up's write path has restored what it wrote, so the line
  says so. The draft stays and *Save* is live again to retry.
- **The lane detail** after a save (4a·6): the provenance sentence `Saved in crateside on 17 Nov,
  from crateside's starter: bar two beats later, +4 ms, clip start bar 65. crateside lends it to
  Drums - Kick.` It names only the edits that changed, in the edit bar's order (bar, nudge, tempo
  as `tempo halved` / `tempo doubled`, clip start), from what the save started as. A later save in
  Live reads `Saved in Live on …` as today.

## 5. Leaving with a draft (4b)

A link inside crateside opens the page's own dialog (`role="alertdialog"`, 440 px, `--raised`,
radius 10, over a 72% scrim): **Leave without saving the warp?**, then `The draft (bar two beats
later, +4 ms, clip start bar 65) is not kept. The four stems stay as they are on disk.` and **Stay**
· **Leave without saving** (`.danger`). Esc is Stay. A reload or a closed tab gets the browser's own
`beforeunload` dialog, whose wording is the browser's.

## 6. Where I added to the brief, or read it one way

- **The edit bar's label is not amber**, unlike the judge form's: the brief keeps amber for
  *waiting for you*.
- **Changed readouts go to `--text`, unchanged ones stay `--text-50`.** The brief gives the readouts'
  words, not how a draft reads against disk at a glance.
- **The as-on-disk readouts** (`…, where crateside put it`, `on crateside's marker`, `as saved`,
  `as on disk`) are mine; the brief gives only the changed forms.
- **The bar readout's second form** (`the marker falls on bar 32, beat 4`) is mine: after `/2` the
  brief's sentence would name a bar 1 before the file's start.
- **`· was bar 1`** after the clip-start readout, so the draft can be read against disk.
- **Save stays live with no draft on a starter or borrowed warp** (accepting it is a save); disabled
  on the maintainer's own.
- **The draft on the lanes** is drawn as `○ draft` in the gutter, a dashed clip-start line and a
  hollow flag. The brief asked for it to be quiet and left the form open.
- **The view keeps its place in the audio** when the grid moves, rather than following its bar
  numbers.
- **The notch, focused, takes the arrows** for itself; the brief's key list already excludes sliders.
- **Saving folds the bar into a line** as the judge form does when it runs; the brief names only the
  done line. The *writing…* chips, Undo's own running and done lines, and the line staying until the
  bar is reopened are mine.
- **Not saved is amber**, the next move being the maintainer's; the brief reserves red for a failed
  write and gives the other no color.
- **The lent stems** appear in *Saves to* as `then lends it again to …` (2a·6) and in the provenance
  sentence; the brief gives the provenance form only.
- **The key hint is dropped at 56rem.**

Nothing in ADR 0107 read worse once drawn.

## 7. After building

Update `docs/design/DESIGN.md` under *Warp view*: the opener and its refusals in the head line, the
edit bar (top line, cells, readouts, foot, keys, 56rem), the draft on the lanes and the notch as a
grip while the bar is open, and the save, Undo, *Not saved* and failure lines; the provenance
sentence under the lane detail. The *Playback* entries under *Differs from the design* are not
touched by this.
