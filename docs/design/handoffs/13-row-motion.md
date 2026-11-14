# Handoff 13: opening and closing rows, keeping your place

Canvas: `Crateside Unified Page.dc.html`, **turn 4 (4a, top of the canvas)**. It's a working demo; use the Proposed/Today switch to compare.
Applies to every expandable row on the Tracks page, and to the stem picker inside the expanded panel (turn 3).

## The rule
What you clicked stays where it is on screen. When a panel opens or closes, the page grows or shrinks around the thing you clicked, and that thing doesn't move.
- Row opening or closing: the fixed point is the **row header** you clicked.
- Picker opening or closing: the fixed point is the **column headers row** (`.stem-heads`: Sample · Ableton key · Where it is). It isn't the opener, because the opener becomes the list and kept lines move into their bands.

The panel height isn't fixed. It grows to fit what's in it.

## Panel heading removed
Drop `.panel-head` (**FILES · SAMPLE STAGING / {name}**, and **WILL BE CUT INTO** before separation) from the expanded panel. It used the same style as `.stem-heads` directly below it, so the two read as one confusing block. Its content is said elsewhere: the row header gives the name and the Where column gives Sample Staging. `.stem-heads` now starts the panel.
- The ` · re-cut pending` marker from 03/3a moves to the right column: it goes before the red lead sentence, in `.pending`.
- The picking hint ("tick what you want on disk; untick to remove") moves to the right column's lead sentence.
- The before-separation hint ("changes apply now — nothing has been made yet") moves to the right column's `.detail`.
- This overrides 03 (3a/3b), whose artboards still show the heading.

## Motion
- Height: 220ms, `cubic-bezier(.33,1,.68,1)` (ease-out). Draw it as `display:grid; grid-template-rows: 0fr → 1fr` with the child set to `overflow:hidden; min-height:0`. This avoids measuring heights in JS.
- Picker lines that appear (band labels, not-made presets, Source) use the same height animation. Their opacity fades in over 154ms after a 66ms delay. When closing, they fade out over 110ms with no delay.
- Ticks fade in with the lines. The row chevron switches instantly (not animated).
- `prefers-reduced-motion: reduce` → 0ms everywhere. The place-keeping rules below still apply.

## Place-keeping (scroll container = the page's scroller)
Set `overflow-anchor: none` on the scroller. The browser's own scroll anchoring conflicts with these rules.

**Opening** (a row, or the picker): on every animation frame until the animation ends (+80ms):
```
bottom = panel.offsetTop + panel.offsetHeight
want   = min(bottom - viewportHeight + 16, anchor.offsetTop - 8)
if want > scrollTop: scrollTop = want
```
- If the panel already fits on screen, nothing scrolls.
- The page never scrolls up, and it never pushes the anchor above 8px from the top of the screen.

**Closing**: an empty spacer after the list (starts at `height:0`). Record `s0 = scrollTop` when the close starts, then on each frame of the animation:
```
need = max(0, s0 + viewportHeight - listHeight)   // listHeight excludes the spacer
spacer = max(spacer, need); scrollTop = s0
```
On scroll events after that, the spacer can only shrink:
```
need = max(0, scrollTop + viewportHeight - listHeight)
if need < spacer: spacer = need
```
So the spacer disappears as you scroll up and can never be grown by scrolling down. Ignore scroll events while an animation is running.

Filtering, searching, or navigating away resets the spacer to 0.

## Notes
1. Multiple rows can be open at once, same as today. The rules work per click.
2. The Dashboard's collapsible sections (brief 11) could use the same rule and motion. They're not drawn here; decide separately.
3. The demo's picker only opens and closes. Ticking and confirming are as specified in 03 (3a).
