# 0097 - a non-4/4 Live warp sets Odd bars once

- Status: Accepted
- Date: 2026-11-16
- Builds on [ADR 0058](0058-a-staged-stem-shows-its-state-in-live.md) (the Odd bars mark) and
  [ADR 0092](0092-a-meter-tag-set-in-live-records-that-the-track-was-heard-through.md) (Meter tags).

## Context

*Odd bars* means the track is not 4/4 all the way through. The maintainer sets it by hand. A warped
`.asd` whose default clip carries another time signature (Pink Floyd – *Money*, 7/4) already says
as much. A clip's time signature does not say the whole track is in that meter, so the warp is
evidence for the mark and not a Meter value.

## Decision

- **The first time a song's stem has a non-4/4 default clip, crateside sets Odd bars as the
  maintainer would have**: through the track-mark store, so the Collection is written to Live by
  the same path as a hand mark.
- **It happens once per song.** `meters.json` keeps the songs it has already done this for
  (`warpOdd`). A mark the maintainer later takes off stays off; the warp does not put it back.
- **A 4/4 warp never clears a mark.** Only the maintainer, or a plain `4/4` Meter tag (ADR 0092),
  takes Odd bars off.
- **The time signature is not shown as the track's Meter.** The Meter line stays the Meter tags the
  maintainer set in Live; the track page shows the Odd bars mark and its button as before. This
  retires the "Meter 7/4, read from the Live warp" line.
- A starter warp is crateside's own reading and never counts (it has no tempo to read).
