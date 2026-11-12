# Handoff 22: playback on Track details, and a view zoomed by its handles

Brief 22, issue #105, ADR 0094 · built against `a235117`
Canvas: `Crateside Playback.dc.html`. **1a** is Track details at full width with the header strip and
the player. **1b** is the page scrolled, the player pinned and the view zoomed to 8 bars with loop
on. **1c** is both at 56rem. **2a** is the player's states, the header strip's variants and the
upload card. **3a** is the warp view **live**: the window drags, zooms and double-clicks, the ruler
and lanes seek, play follows and loop holds. **3b** is every state of the window, numbered. **3c**
is seeking.

The track drawn is the one in brief 20's screenshots (Death Grips – *Takyon (Death Yon)*,
`89F5fpvwPr0`): its key, tempo, stems and judge's windows are from them. The waveforms are drawn
shapes, not its audio. The drifting track, the imported track, the verdict with sections, the
second upload and every retention time are invented. Bracketed text is a placeholder.

**The rule:** the page has one transport, the player strip, and one timeline, the warp view. The
overview strip's window is the view, the loop and the thing playback follows, so it is drawn at
all times and every pointer state has a look. Nothing is newly colored: playing is `--text`, never
teal; teal appears only while a file loads or downloads, red only for a load that failed.

## 1. The header strip

One `--surface` box, radius 10, replacing the heading and the band.

- **Cover**: 152 px square at the left, flush to the strip's top, left and bottom. An `<img>`, not
  a button.
- **Title block**, padding 15px 22px 12px: the title 24/600 (30 line), then the meta line 13.5
  `--text-60`, ending `· YouTube ↗` (underlined, new tab) for the track's upload. An import has
  none: `House · imported file`.
- **The four cells** under a `--line` rule, four equal columns with `--line` dividers, padding
  10px 22px 11px: the caps label (10.5/600), then one line with the value in mono **28/600** (34
  line) and its note beside it on the baseline, 12.5 `--text-60`, wrapping to two lines at most.
  The value keeps the tempo formatter's weights (`~111` at 400) and the pennant keeps its place
  after the figure, at 9×11. *Where it is* is the chip, its sentence and *Why B min ↓* on one line.
- **The foot row** (2a·8): when a cell has more than a note (the tempo strip, *Your warp is typed
  at…*, the invitation and its *Judge from these bars ↓*, the running chip), the strip grows a
  second row under the cells on the same four columns, with a `--line` rule above it. The cover
  stays 152 px at the top; the surface shows under it. A track with nothing extra has no foot row.
- **At 56rem and below** (1c): the cover is 96 px beside the title (21/600), and the cells go two
  by two under both, full width. The cover is no longer as tall as the strip there: a cover as tall
  as a two-row strip would be 220 px, the size the brief set out to leave.
- `← Tracks` stays above the strip. The findings line and the stage flow follow the player strip,
  unchanged; the stage flow is drawn as its own `--surface` strip, since the band it was the foot
  of is gone.

## 2. The player strip

Directly under the header strip, 10 px below, full width: `--raised`, 1px `--text` 10%, radius 10.
Two rows: the controls (padding 9px 14px 8px, gap 16) and the slim waveform (34 px, 14 px side
margin, 11 px below). About 97 px tall. `<section aria-label="Player">`.

Left to right:

1. **Play/pause**: 40 px round, filled `--text` with an `--ink` icon, the only filled control on
   the page. `aria-label` and title `Play (Space)` / `Pause (Space)`. Space toggles anywhere except
   in a text field, a select or an open list.
2. **The position**: mono 14, `bar 19.3.1` at 600, ` · 1:20.80` at `--text-75`, ` / 2:49` at
   `--text-40`. `min-width: 27ch` and tabular figures so it never shifts the picker. No warp:
   `1:20.80 / 2:49`.
3. **The picker**: an `.outline` button 210 px wide (the name, ellipsis, `▾`), opening a listbox
   under it (`--raised`, 1px `--text` 16%, radius 8, soft shadow, 400 px). Rows are 34 px; the one
   playing wears the house *chosen row* style. Details in §3.
4. A flexible gap.
5. **Click**: a pill toggle with a metronome glyph (`aria-pressed`). Off is an outline at
   `--text-75`; on is the house *chosen* pill (`--text` 14% fill, 50% border, 600). Then **Sound**,
   a small `.outline` button naming the sound (`Classic ▾`) that opens a three-row list (Classic ·
   Click · Wood); picking one plays it once, accent then beat, so it is chosen by ear without
   starting the track. Then the **click level**, a 60 px range.
6. **Loop**, after a `--line` divider: the same pill toggle with a loop glyph. On the whole track
   it is disabled and `needs a zoomed view` follows it at `--text-40`.
7. **Volume**, after a divider: a speaker glyph and a 72 px range.

Ranges are native `<input type="range">` with `accent-color: var(--text)`. Volume, click level and
sound are kept in the browser; the click and loop start off on every page load.

**The slim waveform** is the whole file that is playing, as one channel-summed shape at `--text`
50%, the part after the playhead under a 62% `--raised` scrim, with a 1 px `--text` playhead. A
click seeks to that exact spot (cursor `pointer`, title *Play from here*); a press can be held and
dragged to scrub the position, sounding nothing until release.

**Pinned** (1b): `position: sticky; top: 0` on the strip, so it holds its width. Once stuck it
loses its top border and top radius (radius 0 0 10 10) and gains `0 10px 28px rgba(0,0,0,.55)`.
Its first row then leads with a 36 px cover (radius 4) and the track's name (13.5/600, ellipsis,
260 px at most), closed by a `--line` divider, before play. The rest does not move.

**At 56rem** (1c) the controls wrap once: play, position and picker; then click, loop and volume
(volume right-aligned). Pinned, the name gets 230 px and the picker 180, so the first row still
fits. The strip is then about 148 px tall pinned; that is two rows and the waveform, and I kept the
waveform because it is the only way to seek from below the warp view.

### The player's states (2a)

| | state | what shows |
|---|---|---|
| 1 | **Nothing on disk** | The strip at full height. Play disabled (an outline at `--line`), `0:00.00 / –` at `--text-40`, the picker disabled reading *Nothing to play*, click and loop disabled; the waveform row is a dashed `--line` box saying `nothing downloaded yet` at `--text-40`. Volume stays live. |
| 2 | **Loading a file** | The first play and every switch. Play becomes a 2 px teal ring that spins (still under reduced motion); the waveform row is a teal-bordered line, `Loading Vocal - Full…`. The position is kept. Space while loading cancels the pending play. |
| 3 | **Playing** | As 1a. |
| 5 | **Click on, Sound open** | The toggle chosen; the list under the Sound button, 150 px. |
| 6 | **No warp** | Position is time only. `Click` disabled with `no warp yet` after it; no Sound, no level. Loop still works (whole seconds). |
| 7 | **A load failed** | Playback stops; the picker re-reads the disk. The waveform row becomes a red-bordered line (`role="alert"`): `Couldn't load Vocal - Full: the file moved or is gone; try again in a moment.` and a small **Try again**, which asks for the same file. Nothing retries by itself. Picking any file clears it. |

## 3. What the picker holds (2a·3, 2a·4)

Rows, in order: **Mix**, then each stem by its name, only files on disk. A row's status is right
of its name at 12 `--text-50`; a row's one action is a small `.outline` button at its right end,
which does not pick the row.

| the mix is | its row |
|---|---|
| in the working set | `Mix` · `kept until Thu 2:00 PM` · **Keep in the library** |
| in the library | `Mix` · `in the library` (it also has a lane) |
| gone | greyed (`--text-40`, `aria-disabled`), `not on disk` · **Get the mix again**; under it `a download, no GPU · kept at least a day` |
| downloading | greyed, `● downloading…` in teal, no button; under it `it will appear here when it lands` |
| just landed | as *in the working set*; it is not switched to by itself |
| being kept | `● copying to the library…` in teal, until the library holds it |

- With the mix gone the picker opens on the instrumental, or the first stem.
- The list can be closed while the mix downloads; playback carries on.
- *Keep in the library* is the stem-plan change and its re-cut (ADR 0094). If that re-cut waits in
  the GPU queue, the row says `● in line to be copied` instead; the ADR leaves that unmeasured.
- Times of day follow the machine's locale, as everywhere.

## 4. The warp view

Unchanged except as below. Row order: **View** (the overview strip) · ruler · tempo line · *Off the
grid* · the lanes · the key strip · `Chords · #198` (handoff 20's dashed placeholder, 34 px) · the
time axis.

- **Head line**: the zoom pills go. At the right, the readout in mono at `--text`
  (`bars 17–24 · 1:11–1:40`; one bar reads `bar 24 · …`; the whole track reads `whole track ·
  2:49` at `--text-75`), then `‹ ›`, disabled at the track's ends, moving the window by half its
  width (whole bars, at least one). When the playhead is outside the window, *Go to the playhead ·
  bar 31* (`.outline` small) sits left of the readout.
- **The View row's gutter** says `View` and a hint at 10.5 `--text-40` that changes with the state:
  `pull a handle to zoom · double-click for 8 bars` on the whole track, `drag to move ·
  double-click for the whole track` zoomed, `looping these 8 bars` with loop on.
- **The ruler is 40 px** in every state (it was 44 only with the judge form open), so a window's
  tag and the bar numbers never share a line.
- **The playhead**: 1 px `--text` from the ruler's top to the key strip's foot, with the 10×7 head
  on the ruler, above the bands. Not drawn in the plot when it is outside the window. The overview
  strip and the player's slim waveform always show it.
- **A lane's gutter** wears a 14 px speaker glyph at its top right while its stem is what plays.
  That is separate from the selected lane: with the mix playing and no mix lane, no gutter wears
  it and the selected lane keeps its detail.
- **Handoff 20's gutter transport is not built.** The ruler's gutter says `Bar` again.

### The window (3a live, 3b numbered)

The overview strip is 34 px on `--surface`, radius 3, holding the whole file's shape at `--text`
30%. `touch-action: none; user-select: none`.

**Anatomy.** The window is a box from its start to its end: fill `--text` 7%, a 1 px `--text` 50%
line top and bottom. Outside it, a `--bg` 66% scrim. Each **handle** is a 6 px bar centered on an
edge, the strip's full height, `--text` 70%, radius 2, with a 1×10 px `--ink` notch. Zoomed out the
window spans the track, the handles sit on the track's ends and nothing is dimmed. Bars centered on
the edge are what make one bar workable: at 17 px a bar there is still 11 px of body between them.

**Targets.** A handle's target is 16 px, 8 either side of its edge, and wins over the body. The
outer 8 px exist at the track's ends too, past the plot (the 12 px column gap on the left, the
panel's edge on the right). In a window under 32 px wide the inner part of each target shrinks to a
quarter of the window's width, so the middle half still grabs the body.

**Snapping.** Edges land on bar lines, plus the file's first and last sample. Without a warp, on
whole seconds. The window is at least one bar wide (4 seconds without a warp).

| 3b | state | look and behaviour |
|---|---|---|
| 1 | at rest | as *Anatomy* |
| 2 | hover | body: fill 12%, cursor `grab`. Handle: bar at full `--text`, cursor `ew-resize`. |
| 3 | press | `pointerdown` → `setPointerCapture`, and remember the grab offset (pointer x minus the window's start, or minus the edge), so nothing jumps. At once: body fill 17% and full-strength lines, or the handle at full `--text` with a 2 px `--text` 28% halo; the cursor is locked to `grabbing` / `ew-resize` on the whole page (a class on `<body>`), and text selection is off. **Nothing moves for the first 3 px.** A press released inside 3 px is a click: no change, no report. |
| 4 | dragging the body | the window keeps its width and steps bar by bar; a tag above its center names its bars (`21–28`: mono 11/500, `--ink` on `--text`, radius 3). Every row redraws at each step and the head line's readout follows, all in the browser. Only the pointer's x counts; it may leave the strip. |
| 5 | body at the track's end | the window stops, width kept; the pointer goes on alone and picks it up again only on coming back to the grab point |
| 6 | dragging a handle | that edge steps bar line by bar line, the other stays. The tag sits over the moving edge: `from bar 13`, `to bar 28`. |
| 7 | minimum width | a handle pulled into the other stops one bar short. It does not push the other edge and the two never swap. The stopped handle keeps its halo. |
| 8 | handle at the track's edge | stops at the file's end; the pointer goes on without it |
| 9 | release | `pointerup`: the tag goes, the window rests (hovered, the pointer is still on it) and the new bars are reported to the server **once**. `lostpointercapture` with no `pointerup` counts as a release. **Esc** during a drag, or `pointercancel`, puts the window back where the press found it and reports nothing. |
| 10 | double-click | zoomed → the whole track, remembering the zoom; whole track → that zoom. With none remembered (a fresh page), 8 bars around the place clicked. The remembered zoom lives in the browser for the page's life. |
| 11 | press outside the window | as built: the window comes to the pointer, centered and snapped, and the same press carries on as a body drag. Zoomed out there is no outside. |
| 12 | follow | while playing, not looping, the playhead reaching the window's right edge moves the window on by half its width (whole bars, rounded up, at least one), in one step with no slide; the playhead is then at its middle. At the track's end the window stops against it. A follow step is not reported to the server. |
| 13 | a view left behind | the audio never notices a drag. From press to release there is no follow. If the release leaves the playhead outside the window, the view stays put, the plot shows no playhead and *Go to the playhead* appears. Follow starts again when the playhead is next inside the window: by that button, by a seek, or by playing into it. `‹ ›` are the same. |
| 14 | loop on | the window's top line becomes 3 px at full `--text`. The playhead returns to the left edge at the right one; no follow. Switched on with the playhead outside the window, playback moves to the window's start at once. |
| 15 | dragging while looping | until release the audio loops the bars it had, drawn as a dashed 1 px `--text` 50% ghost, while the window shows where the loop will be. On release the loop is the window; a playhead then outside it moves to its start. Widening to the whole track switches loop off. |
| 16 | no warp; keyboard | tag gives times (`1:12–1:40`). Each handle is `role="slider"` (←/→ one bar, Shift four, Home/End), the body a button (←/→ move a bar, Enter as a double-click), with a 2 px `--text` focus ring 2 px out; a keyboard move reports on key-up. |

No Ctrl+scroll and no click-and-hold zoom, as the brief says.

### Seeking (3c)

- **The seek cursor**: a 16 px glyph, a 1.5 px stem with a filled triangle to its right, hotspot
  on the stem, in `--text` with a dark edge. Used on the ruler, the lanes and the key bands.
- **The ruler**: a faint line (1 px `--text-30`) from the ruler to the key strip at the start of
  the bar under the pointer; a click seeks there. Playing carries on from it; paused stays paused.
  Off while *Judge from these bars* is open, and on a window's tag. Without a warp the line follows
  the pointer and the click is exact.
- **A lane**: the faint line follows the pointer in that lane only. A click seeks to the spot,
  selects the lane and switches to its stem at that position (state 2 while it loads, the playhead
  waiting at the new spot).
- **A key band** is a `<button>`: hovered, its outline goes to 90%; its title is
  `chorus · E minor · ▶ from 1:53`; a click seeks to its start.
- **The drawer's *Play window***: `.outline` small with a play glyph, at the drawer's foot left of
  `‹ 2 of 3 ›`. It seeks to the window's start and plays. With loop on it also sets the view to the
  window, so the window is what loops; with loop off the view moves only if the start is outside
  it. It reads *Pause* while that window plays. The page does not scroll.
- **A seek to a spot outside the window** (from the slim waveform, a band, a drawer) brings the
  window, at its width, to start on the bar that holds the spot. With loop on the loop moves with it.
- The overview strip never seeks; nor do the tempo line, *Off the grid*, the chord placeholder,
  the time axis or the gutters.

## 5. The upload card (2a·9)

The embed goes. A card is `--surface`, radius 8, padding 10px 12px, two lines at 12.5: the role at
`--text-60` with `YouTube ↗` at the right, then the video ID in mono at `--text-75`. Several
uploads are several cards, 8 px apart, under `Uploads`. Role wording is as built; the second
card's role on the canvas is invented.

## 6. Where I added to the brief, or read it one way

- **The slim waveform is its own row**, not inline. Inline it had 200 px left at the page's width
  once pinned, too little to seek in.
- **Follow has no control.** The brief names no toggle, so it is always on and yields in three
  cases: looping, a drag in progress, a view the person moved away from (3b·13).
- **The loop's bounds change on release, not during the drag** (3b·15). Changing them under the
  playhead at every bar step would make the audio jump while the hand is still moving.
- **A press outside the window** keeps today's jump-and-slide (3b·11); the brief does not mention it.
- **Esc cancels a drag** (3b·9), and the first double-click on a fresh page takes 8 bars (3b·10).
- **Loading** (2a·2), **Try again** (2a·7), the Sound list's preview and scrubbing the slim
  waveform are mine.
- **The header's foot row** (§1) is mine: the brief's strip has no room for what the band's cells
  hold today on a drifting or withheld track.
- **Minimum of 4 seconds** for a window without a warp is a guess at what is still draggable.

Nothing in ADR 0094 read worse once drawn.

## 7. After building

Update `docs/design/DESIGN.md`: under *Warp view*, the head line without pills, the View row and
the window's states, the 40 px ruler, the playhead, seeking and the speaker mark; a *Player*
section; under *Track details*, the header strip; under *Side stack*, the upload card. Clear
*Differs from the design* entries this replaces: handoff 14's *The overview strip is dragged, not
only clicked*, handoff 05's *Upload players show no still*, and handoff 20's *Not built … the
transport and the playhead*.
