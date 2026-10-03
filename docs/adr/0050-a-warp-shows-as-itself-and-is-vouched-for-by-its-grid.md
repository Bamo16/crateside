# 0050 — a warp shows as itself, and is vouched for by its grid

- Status: Accepted
- Date: 2026-10-02
- Supersedes the *nominal tempo* in [ADR 0049](0049-a-bpm-shows-only-the-precision-crateside-can-vouch-for.md).
  The rest of 0049 (one tempo type, one formatter, the marks) stands. Built in #96.

## Context

ADR 0049 gave every steady tempo a *nominal* value: a warp or reading within 0.003 BPM of a whole
or half number showed that number. The premise was that a track made at 128 should read 128 when a
snapped warp lands at 128.002.

The user rejected it on 2026-10-02: crateside's analysis is now accurate enough to say what a warp
is, and the snap replaces that with a guess that the producer used a round number, made on no
evidence about the track. It also hides warps that are off. *Blow the Whistle* was typed at 100 and
is 99.98; a warp in #96 shows `180` while running at 179.983, drifting about 23 ms over four
minutes (#91, [note](../notes/2026-10-02-the-six-warps-in-91.md)).

`GridCheck` (`Tempo/GridCheck.cs`) now measures what the snap guessed at. It lays a warp's grid
over the stronger half of the track's transients and reports each 10 s window's offset, the drift
per minute, and any step. Window noise is 1.5 ms on a drum stem and 2.3 ms on an instrumental
(median over 12 songs, measured 2026-10-02, on #96).

## Decision

- **A warp shows as itself**: its own tempo, two decimals with `.00` dropped, as 0049's marks
  write it. No snap, for a warp or for a reading.
- **A steady warp is vouched for when it passes `GridCheck`**: no step, and drift and spread within
  the windows' noise. #96 sets the thresholds from the library's steady warps before it ships, and
  records them in its note.
- **A steady warp that fails shows as approximate** (`~`, whole) until #96's *off its tempo* state
  has a design, and its note says how far off it sits and which way. #96 also offers to correct it.
- **A track with no warp shows crateside's reading as read**, vouched when its 30 s windows agree
  within 0.02%, as before.
- **Starter warps take the tempo the transients fit best** (`GridCheck.Refine`, searching around
  crateside's reading), not the reading rounded to the grid.
- Drifting, mostly steady and changing warps are read as 0049 says; `GridCheck` applies to the
  steady part.

## Consequences

- **A bare figure on a warped track means the warp holds against the audio**, not that it sits
  near a round number. `128` now means the warp is 128.00 and its transients stay on the grid.
- **Snapped warps a hair off show their hundredths** where they round differently (`127.98`), and
  those that drift lose the bare mark. That is the point: they are the ones to fix.
- **`TrackTempo` loses `Nominal` and its snap constants**; the raw figures it kept become the
  figures shown.
