# crateside: a warp that sits off its tempo, and the button that fixes it

Issue: #96

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against the commit that adds this brief. The rules as built are in `docs/design/DESIGN.md`
(Tempo, and the Warp view), and your canvases are in `docs/design/canvases/`. The first build
below is deliberately plain, made only of what already exists; this brief asks what the state
should look like.

---

## Where it stands

crateside checks a **steady** warp (one tempo all the way through) against the audio: it lays the
warp's grid over the track's transients and measures how far they drift from it. A warp that drifts
by more than the measurement can tell apart is **off its tempo**: it runs a few thousandths of a
BPM fast or slow, and over a track that adds up to tens of milliseconds. A warp that holds is
vouched for, and shows its bare figure. One that does not is approximate, with a note.

Today the new state uses only existing pieces:

- **The figure** is approximate: `~` and weight 400, a whole number (`~180`).
- **The note** (song page, tooltip) reads `off its tempo · runs 0.017 fast · drifts 23 ms · from
  your warp`. Two other unconfirmed states read `steps in its grid · from your warp` (the grid
  jumps partway through, which is a different fix) and `grid not confirmed · from your warp`
  (too few transients, or no tempo that holds).
- **The Tracks flag** beside the title is the existing neutral chip: `off its tempo` with `0.017
  fast` in mono where *drifts* puts its percentage.
- **In Track details' warp view** the selected stem gets a third row in the controls, beside
  *Re-apply* and *Let Live analyse*: **True up the warp**, with the button *True up*, a status line
  (`Runs 0.017 fast and drifts 23 ms over the track. Retypes it to 179.983 and lines it up with
  the drums. Yours is backed up first.`), and the same confirm and done lines the other two use.

## What the user does

- **Sees which tracks are off** in the Tracks table, without opening them, and how bad (23 ms is
  worth fixing; 4 ms is not).
- **Understands why a figure carries a `~`** when the warp is theirs and they typed it: the song
  page must say it is the *warp*, not crateside's guess, that is being questioned.
- **Fixes it** from the warp view: one action, a confirm that says what will move and by how much,
  and a line afterwards that says it is done and that the old warp is backed up.

## Questions for the design

- Is a flag enough on the Tracks row, or does *how far* off need more than the mono text?
- Should *off its tempo* look different from *drifts* (the track's own tempo wandering)? They are
  opposite problems: one is the warp's fault, the other the music's.
- The three unconfirmed notes are plain text for now. Do they need distinguishing marks?
- Where, if anywhere, should the offset in milliseconds be drawn on the warp view's lanes?

## Leave room for

A batch action (true up every track that is off) later; nothing to design for it yet.

## Raised by the maintainer while checking the first build

- The flag beside the title reads `off its tempo0.022 slow` (the gap between words and amount was
  missing; fixed with a margin). The larger question: should the indicator sit beside the title at
  all? It belongs with the BPM. There is no room for a chip there, but a small mark (a dot or a
  glyph) would fit, with the detail on hover.
