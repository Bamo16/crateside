# 0107 - A warp saved in crateside is the maintainer's own

- Status: Accepted
- Date: 2026-11-17
- The second half of [#105](https://github.com/Bamo16/crateside/issues/105), after
  [ADR 0094](0094-playback-is-one-file-at-a-time-in-the-browser-with-a-synthesized-click.md); the
  layout is [brief 24](../design/briefs/24-warp-editing.md). Folds in
  [#103](https://github.com/Bamo16/crateside/issues/103) (octave controls for every stem at once).
- Amends [ADR 0037](0037-a-saved-default-clip-is-a-finished-warp.md) and
  [ADR 0043](0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md): a saved default clip
  may now be written by crateside on the maintainer's word.

## Context

A starter warp (ADR 0048, 0054) leaves the maintainer five small edits, all made today in Live:
move the bar a beat so the downbeat is beat 1, nudge the marker, halve or double the tempo, set the
clip start to the first verse's bar, and save the default clip. With playback built (ADR 0094), the
maintainer hears the click on crateside's grid land on the beat as well as Live's, by ear on
2026-11-17; how the two compare was not measured.

#103 asked for Live's `/2` and `×2` applied to every stem at once, and for the chosen octave to be
a warp crateside returns to, which had no name.

## Decision

- **The edits are a draft, then one save**, as in Live's clip view. The draft lives with the page:
  the grid, bars and click follow it at once, nothing is written until *Save the default clip*, and
  leaving the page asks first. Nothing persists a draft.
- **What can be edited**: the bar (the grid moves a whole beat under the audio, the marker keeping
  its time), the marker's time (1 ms steps), the tempo by `/2` and `×2`, and the clip start (whole
  bars). The clip end stays at the file's end and Loop off. Markers are not added or removed.
- **The clip start is a bar number.** Moving the grid or nudging keeps it on its bar line, so the
  edits commute.
- **`/2` and `×2` are anchored on the clip start**: it keeps its bar number and its moment in the
  audio, and the marker's beat scales about it. Anchoring there, on a bar line, is what avoids the
  results #103 calls strange.
- **Any warp the upload's stems share can be edited** (starter, borrowed, or the maintainer's,
  steady or not, in staging or the library), since every edit moves or scales the whole grid. A
  clip with Warp off, no sidecar, or authority stems holding different warps (True up's *Disagree*)
  is not offered. A stem warped on its own is never written.
- **Save writes the group** the way True up does: every sidecar stamped when the draft began and
  checked again before writing, each backed up, written with the saved-clip flag set, then every
  stem crateside lent the warp to written again. If any sidecar changed since, nothing is written
  and the page says which; there is no merge. **Undo** restores the backups and re-lends, for as long
  as the page stays open.
- **A warp saved in crateside is the maintainer's own.** It reads *warped*, the derived Collection
  follows, the track becomes *ready to move*, ADR 0043's rule that crateside never replaces it
  unasked applies, and it is what crateside lends to siblings and re-cuts (ADR 0045). crateside
  records the save (when, and the edits made from what it started as) so the lane detail can say
  *Saved in crateside*; that record is history, not ownership, and a later save in Live supersedes
  it.
- **#103's "warp crateside returns to" is the maintainer's saved warp.** No new kind of warp and no
  new name: once the octave is saved, lending and *Re-apply* carry it. Several saved warps per song
  stays possible and undesigned.
- **Moving the track into the library stays in Live's browser.**

## Consequences

- A starter can be finished with Live closed. Live does not reload a clip already in an open set;
  the saved file is what its browser and the next drag-in use.
- ADR 0037's caveat widens: a save in crateside is only as good as the ear behind it, and it is the
  maintainer's habit, not the tool, that keeps an early save from reading as finished.
- The edit uses True up's write path, so a failure part way restores what was written, as it does.
- Whether crateside's click agrees with Live's to the millisecond is unmeasured; a measurement would
  play both into one recording.
