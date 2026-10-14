# 0054 — a starter warp's marker sits on a beat the loud transients place

- Status: Accepted
- Date: 2026-10-13
- Amends [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md), which put the starter
  warp's one marker at the start of the audio. Builds [#72](https://github.com/Bamo16/crateside/issues/72)
  for [#101](https://github.com/Bamo16/crateside/issues/101).

## Context

A starter warp set the tempo and left the phase to the user, who moved the marker onto a beat by
hand. The user's bar is any beat of the grid, not the downbeat (2026-10-13): beat 1 and the octave
stay theirs, and a marker already on a beat saves most of the move.

Two measurements, on the 353 steady single-marker warps of the library against the user's own
warps, with the pinned beatdetect build ([first note](../notes/2026-10-13-placing-the-marker-from-many-transients.md),
[second](../notes/2026-10-13-the-marker-from-crateside-s-own-transients.md)):

- beatdetect's own beat offset (what `StarterGrid` reads) puts the marker within 10 ms on 204 and
  half a beat off on 48.
- Folding crateside's own transients onto one beat at the starter's tempo, the louder ones weighted
  to the fourth power, puts it within 10 ms on 275 and half a beat off on 31 (the built code, run on
  the same tracks). The power was chosen on this set; 3 to 6 all do well.

## Decision

**The starter warp's marker is written on the first beat the transients place** (`GridCheck.FirstBeat`),
as beat 1, so the audio's first moment is the fractional beat before it and the clip runs from there
to the end of the file. With fewer than 32 transients nothing is placed and the marker stays where
ADR 0047 put it: beat 0 at the start of the audio.

- **The whole group is timed from one stem**: the instrumental its tempo is read from. Timed each
  from its own transients, *How Will I Know*'s drums, instrumental and vocal got three tempos
  (119.5175, 119.5160, 119.5405) and three markers (0.18 s, 0.14 s, 0.43 s), and Live would not edit
  them together (*3 audio clips with different Warp Markers*). ADR 0047's rule that every stem gets
  the same marker holds.
- **Beat 1 is a beat, not a bar line.** Which beat starts a bar is unknown and stays the user's.
- **The tempo is the one the starter already refines** (ADR 0050); the phase is taken at that tempo.
- **The octave is not touched.** The fold is at the starter's tempo whichever octave that is.
- **Not verified in Live** at the time of writing: a first marker at a nonzero time with a fractional
  clip start is how a user's saved warp looks, but a starter written this way has not been opened.

## Consequences

- About one track in eleven still lands half a beat off (31 of 353 on the corpus). It is moved the
  same way as before.
- The filter's own beat offset is not used for placement. `StarterGrid`, which reads it, is not
  wired into the writer; it stays for the downbeat, its confidence gate and the moving flags.
