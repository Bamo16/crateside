# crateside: playback on Track details, and a view you zoom by dragging

Issue: #105

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

This brief was written against the commit that adds it. The rules as built are in
`docs/design/DESIGN.md` (*Warp view*, *Track details*, *Side stack*, and *Differs from the
design*). The page's canvases are *Crateside Track Page* and *Crateside Warp View* in
`docs/design/canvases/`; handoff 20 (`docs/design/handoffs/20-track-page.md`) is the latest design
of the page. Screenshots of the page before this work are in
[`20-track-page/`](20-track-page/). The decisions behind this brief are
[ADR 0094](../../adr/0094-playback-is-one-file-at-a-time-in-the-browser-with-a-synthesized-click.md);
bars are numbered as [ADR 0072](../../adr/0072-bars-are-numbered-as-lives-ruler-numbers-them.md)
says. Terms (stem plan, separation source, warp, window) are in `CONTEXT.md`.

---

## Why

The maintainer finishes every starter warp in Ableton Live: he drags the stems in, moves the one
marker a beat or two so the downbeat sits on bar 1, trims to the verse and saves the clip. The
grid is judged by ear, against Live's metronome. crateside will take over those edits later; this
brief is the first half, **hearing the track in crateside**: a player, a playhead on the warp
view, a metronome click on the warp's grid, and a view navigated the way Live's clip view is.

The model is Live itself: a transport fixed at the top of the window, and a clip view below where
the playhead moves over the waveform.

## The page today

From the top: a **heading** (a 200px cover, the title and a meta line), the **findings** line, a
**band** of four cells (*Key* · *Confidence* · *BPM* · *Where it is*), the **stage flow** strip,
then two columns. The main column holds the **sample list**, the **warp view** (ruler, tempo line,
*Off the grid*, one lane per stem with Live's waveform overview, the key strip, the time axis; zoom
pills *Whole track · 32 bars · 8 bars*; an overview strip, drawn only when zoomed, dragged to move
the view), the **key picker** and the **evidence panel**. The side stack holds, among other things,
**Uploads**: a card per YouTube upload with an embedded player (blank until pressed), the video ID
and *YouTube ↗*.

Handoff 20 reserved a 28px play button and `0:47.20 / 2:48` in the ruler's gutter, and a 1px
playhead with a 10×7 head. **The gutter transport is replaced by the player strip below; the
playhead stays.**

## What is decided

### The header strip

- **One strip**: the cover at the left, square and as tall as the strip (smaller than today's
  200px; the size is yours), then the title and meta line, and under them the four cells.
- **The meta line ends with `YouTube ↗`** for the track's upload, opening in a new tab. An import
  has none.
- The cover is a picture, **not a play button**.
- The findings line and the stage flow move below the player strip, unchanged.

### The player strip

Directly under the header strip, full width. **When the page scrolls, it pins to the top of the
window** and stays there; pinned, it gains a small cover thumbnail and the track's name at its
left, since the header has gone. It holds:

- **Play/pause.** Space toggles it anywhere on the page except in a text field.
- **The position**: `bar 34.2.1 · 1:12.40 / 3:48`, bars in Live's numbering (ADR 0072; `-1.3.1`
  before beat 0). Without a warp, time only.
- **What plays**, a picker: *Mix*, then each stem by its name. One file plays at a time; switching
  keeps the position. Selecting a lane in the warp view also picks its stem.
- **The click**: a toggle, off each time the page opens; a *Sound* choice, **Classic · Click ·
  Wood**; a click level. Without a warp the toggle keeps its label and says *no warp yet* at
  `--text-40`, as warp-view controls that do not apply already do.
- **Loop**: a toggle, off by default (below).
- **A volume** for the player. Volume, click level and sound are remembered in the browser.
- **A slim waveform of what is playing**, the whole file, with its own playhead; clicking it seeks
  to that exact spot. It is the only waveform on the page before the track has stems, and the only
  one for the mix when the mix has no lane.

### What the picker holds, and when the mix is gone

The picker lists only files that are on disk:

- **Mix** whenever the source is on disk. A *kept* source (one the stem plan keeps in the library)
  also has a lane in the warp view; a source held only in the working set has no lane, so the
  picker is where the mix lives. Its line says how long it stays: *Mix · kept until Thu 14:00*, and
  **Keep in the library**, which keeps it for good (it becomes a library sample like the stems, with
  a lane). A mix that is in the library reads *Mix · in the library*.
- **When the mix is gone**, the picker opens on the instrumental stem (or the first stem). *Mix*
  stays in the list greyed, offering **Get the mix again**: a download, no GPU, kept at least a day.
  While it downloads, the entry says so; once it lands, it plays and offers *Keep in the library*.
- **Each stem** while its file exists.
- **Nothing on disk at all** (a track not yet downloaded): the strip still shows, play disabled, the
  reason at `--text-40` (*nothing downloaded yet*), so the page does not jump when audio arrives.
- **A load that fails** (the maintainer moved the file in Live's browser at that moment, or deleted
  it) stops and says so in the strip: *Couldn't load Drums: the file moved or is gone; try again in
  a moment*. The picker refreshes from what is on disk. Audio already playing is in memory and is
  not interrupted by a move.

### The warp view as Live's clip view

- **The playhead** (handoff 20's: 1px `--text`, not teal, a 10×7 head on the ruler) runs through
  every row, from the ruler to the key strip.
- **Seeking:**
  - **The ruler** seeks to the **start of the bar** clicked, as Live's scrub area does with its
    1-bar quantize. Hovering it shows a **seek cursor** (Live shows a speaker) and a faint line at
    the bar start where playback would land. Not while *Judge from these bars* is open (the ruler
    then draws ranges, with its crosshair), and not on a window's tag (which opens its drawer).
    Without a warp the ruler counts time and seeks to the exact spot.
  - **A lane** seeks to the exact spot clicked and picks its stem as what plays (a lane click today
    selects the stem's detail; both happen).
  - **A key-strip band** seeks to its start; its title adds `▶ from 1:53`. A section named in the
    gutter with no time does nothing.
  - **A window's drawer** gains *Play window*: seeks to the window's start and plays (looping it
    when loop is on). The page does not scroll.
  - Nothing outside the timeline seeks.

### The view: always a window, zoomed by its handles

**The zoom pills go.** Replace them with Live's way:

- **The overview strip is always drawn**, with **the window always on it**. Zoomed out, the window
  spans the whole track.
- **Drag the window's body to move it; drag a handle on either side to zoom.** Edges snap to bar
  lines when there is a warp (whole seconds without one). The window is at least 1 bar wide.
- **Discoverable without the pills**: visible grip handles, resize and move cursors, and the head
  line keeps its readout (`bars 61–68 · 2:10–2:27`). **Double-click the window** to switch between
  the whole track and the last zoom.
- **No Ctrl+scroll and no click-and-hold zoom**: in a browser Ctrl+scroll is the page's own zoom.
- **`‹ ›` stay**, moving the window by half its width.
- **Follow**: while playing, when the playhead reaches the window's right edge, the window moves on
  by half its width. Zoomed out, there is nothing to follow.
- **Loop** plays the window over and over while it is narrower than the track; zoomed out, the loop
  toggle says it needs a zoomed view. Moving or resizing the window moves the loop. The window does
  not follow while looping. Live's own loop brace on the ruler stays a fact about the saved clip and
  is not the playback loop.

**Draw every state of the window, including during playback**: hover over the body and over each
handle, press, dragging the body, dragging a handle, release, double-click, the minimum width, a
handle pushed against the track's edge, and the playhead crossing the window's edge with follow
on. The first draggable overview strip was hard to build after its design arrived, mostly in what
the browser does between press and release; a design that names each state settles that before
building. The window lives in the browser and reports to the server only on release, as the judge
form's ranges do today.

### The click's sounds

Classic, Click and Wood are **synthesized in the browser**, named as Live names its metronome
sounds: Classic a short pitched beep, Click a very short bright transient, Wood a woodblock. Each
has an accent voice for the first beat of a bar. Nothing of Live's is copied or read. The design
only needs the control; the sounds are tuned by ear while building.

### The side stack's uploads

**The embedded YouTube player goes.** Each upload card keeps its role, video ID and `YouTube ↗`, so
a track with several uploads still lists them all.

## Not in this brief

- **Editing the warp** (moving the marker, setting the clip start and end, saving the default clip):
  the rest of #105, briefed once playback has proven its timing.
- **The chord lane** under the key strip: chord times are not stored yet (#198). Keep handoff 20's
  placeholder, its label now `Chords · #198`.
- **Playing from the Tracks page**: only Track details plays; leaving the page stops it.
- **Several stems at once** (mute and solo).

## What to send back

The header strip and player strip at full width and at the 56rem breakpoint, pinned and unpinned;
the player's states (nothing on disk, mix gone and fetching, playing a stem, click on, no warp,
load failed); the warp view with the always-on window and its interaction states above; the upload
card without its embed. Update the *Crateside Track Page* and *Crateside Warp View* canvases or
make a new one, as suits.
