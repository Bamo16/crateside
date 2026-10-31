# 0050 — a warp shows as itself, and is vouched for by its grid

- Status: Accepted; amended 2026-10-30 (below): a true-up retypes only the steady stretch that ends
  the warp, and is refused if it would move anything audible further than the drift it removes
- Date: 2026-10-02
- Supersedes the *nominal tempo* in [ADR 0049](0049-a-bpm-shows-only-the-precision-crateside-can-vouch-for.md).
  The rest of 0049 (one tempo type, one formatter, the marks) stands. Built in #96.

## Context

ADR 0049 gave every steady tempo a *nominal* value: a warp or reading within 0.003 BPM of a whole
or half number showed that number. The premise was that a track made at 128 should read 128 when a
snapped warp lands at 128.002.

The user rejected it on 2026-10-02: crateside's analysis is now accurate enough to say what a warp
is, and the snap replaces that with a guess that the producer used a round number, made on no
evidence about the track. It also hides warps that are off. Too $hort – *Blow the Whistle* was typed at 100 and
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

**Amended 2026-10-30: a true-up retypes only the steady stretch that ends the warp, and checks
where the audio lands, not just its phase** ([#99](https://github.com/Bamo16/crateside/issues/99)).

- **The case that showed it** (MEASURED 2026-10-30, from the backups the true-up kept). On
  2026-10-19, *True up all* rewrote the three sidecars of a song whose hand warp has a quiet intro
  at other tempos (Eminem – *Without Me*, `tqxRidAWER8`): markers at about 170, then 105 and
  112.84 BPM, and a steady 112.2503 from beat 6 (4.8588 s); the default clip starts at beat 86.93.
  The decision read the tempo across the clip only, found it steady, and laid every marker on one
  112.25285 line through the first one (beat −4, 0.7205 s). Beat 6 moved to 6.063 s, 1.203 s or
  2.253 beats later, so the hits landed a 16th early and the bars two beats off. The rephase check
  passed it, because `GridCheck` measures where transients sit within a 16th: 2.253 beats read as
  0.003 of one. The maintainer restored the three files from the backups.
- **The steady stretch** is the run of segments under the default clip, widened over each segment
  beside it within 0.05 BPM of the clip's tempo at that end (`TrueUpDecision.SteadyStretch`).
- **The retype is anchored on the stretch's first marker.** Every marker before it stays where the
  user put it, in seconds and in beats; the stretch is laid on the new tempo from that marker, and
  only the stretch takes the rephase shift.
- **The stretch must run to the last marker.** A warp whose steady stretch is followed by another
  tempo, whether between two unsteady parts or before one, is left as `NotSteady`. Retyping a
  stretch in the middle would move everything after it, which needs its own design; it is refused
  deliberately, not overlooked.
- **The safety check compares absolute positions.** Before a retype is offered, the clip's start
  and end, its loop and every marker inside the clip are found in the audio at the same beat on
  the old warp and the new. If any moves by more than the drift the retype removes, plus the
  rephase shift, plus 5 ms, the retype is refused (`DoesNotHold`). The 16th-note check stays for
  what it measures, the phase.
- **What it changes** (MEASURED 2026-10-30, replaying the fixed decision on each of the 125
  sidecars that run rewrote, against its backup): *Without Me*'s three are the only ones placed
  differently. Every other warp is one steady stretch from first marker to last, gets the same
  tempo, and lands where the run put it (0.00 ms apart); the most any of them moved an audible
  point was 25.7 ms, within its drift and shift. On *Without Me* the fixed retype keeps the intro
  and moves the body at most 6.5 ms, the drift it removes.
