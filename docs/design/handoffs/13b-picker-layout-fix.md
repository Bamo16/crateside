# Handoff 13b: picker layout fix (corrects the build of 03 + 13)

Reference: `Crateside Unified Page.dc.html` from `crateside-design-13-row-motion.zip`, turn 4 (4a). Open the last row, then click **Change what is cut**. The build should behave the same way.

## What drifted
Opening the picker in the build swaps the panel for a different layout. The one-column list becomes a four-column grid (INSTRUMENTAL · VOCAL · SOURCES · WARP GUIDE), the column headers disappear, and the whole right side (actions, NEXT, GENRE, FAMILIARITY, NAME, START OVER) is replaced by one hint and a Close button. Since every line moves to a new place, no animation can make that feel smooth. The spec in 03 says: "Opener expands in place; the list becomes the full inventory."

## What it should be
1. **One vertical list, same as the closed state.** The same `.stem` grid (`22px 1fr 120px 140px 1fr`) and the same `.stem-heads` row (SAMPLE · ABLETON KEY · WHERE IT IS) stay visible in both states. No multi-column band layout.
2. **Lines already on screen stay the same lines.** Open the picker, and each on-disk line (Instrumental - Full, source, Drums) stays the same element. It only moves down by the height of any band label or new line inserted above it. Don't re-render the list as new elements.
3. **The lines that appear** (band labels, not-made presets, Karaoke, Add a source, Adopt a file) grow in with the height animation from 13 (`grid-template-rows 0fr→1fr`, 220ms, ease-out, opacity fading in after a 30% delay).
   - Band order, top to bottom: **VOCAL**, **INSTRUMENTAL** (incl. Karaoke), **WHOLE TRACK** (Source, Drums warp guide), **OTHER SOURCES** (added sources + the Add a source / Adopt a file buttons in the dashed opener style).
   - Band labels: mono 10.5px/500, text-35, padding `8px 12px 4px`.
   - Within each band, keep the order the lines have when closed.
4. **Tick cells** are there in both states (22px column). When closed they are empty (opacity 0). When picking they fade in with the lines, so the text in the sample column never moves sideways.
5. **The opener** ("+ Change what is cut") collapses to 0 height while picking, with the same animation. It doesn't jump.
6. **The right side stays.** The middle and right columns (actions, NEXT, GENRE, FAMILIARITY, NAME, START OVER) don't disappear or reflow. While picking, only the **action row and NEXT block** are replaced, in the same spot, by:
   - the picking lead sentence: "Tick what should exist. Nothing changes until you confirm."
   - the diff summary (03, Right column)
   - **Confirm** / **Cancel**

   Everything else in those columns stays as it is.
7. **Cancel** runs everything in reverse: the new lines shrink out and the opener grows back. The 13 closing rule applies (bottom spacer, anchor = `.stem-heads`).

## Check
Record the page as you open the picker on the bottom row. The SAMPLE / ABLETON KEY header shouldn't move at all, the kept lines should only move down, and nothing on the right should move except the action block, which swaps in place.
