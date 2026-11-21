# 0044 — a borrowed warp keeps Live's transients where Live found them

- Status: Accepted; amended by [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md), which tells crateside's transients from Live's by fingerprint
- Date: 2026-10-02
- Amends [ADR 0043](0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md). Builds
  [#54](https://github.com/Bamo16/crateside/issues/54).

## Context

ADR 0043 writes a borrowed sidecar with transients crateside detected, and ships lending switched
off because the detector falls short on vocals (69% of Live's within 25 ms, against a bar of 81%).

On 2026-10-02 the user set out how the transients are actually used:

- **Every warp is Complex mode**, where transients do not change playback. On a borrowed warp
  they are only snap points in the clip view.
- **A group is warped on the drum stem.** The user mutes it, listens to the instrumental and
  vocal, and places markers on the drum stem's transients. The warp is the group's, and the
  instrumental carries it.
- **crateside never warps a vocal on its own.** Whether a vocal needs a hand warp is the user's
  decision, and the user then does it by hand.
- **Recuts are mostly instrumentals**, usually because a vocal bled through, and sometimes vocals.
  The old cut is discarded afterwards, so it is still on disk when the new one arrives.

Live has often already detected the transients for this sound. On 2026-10-02, in 413 of 414
uploads with two or more cuts that Live had analyzed, every cut had the same frame count
(measured).

## Decision

**A borrowed sidecar's transients come from the first of these that applies:**

1. **The stem's own analysis**, when Live analyzed this exact file (its `OriginalFileSize` and
   frame count match) without a saved clip.
2. **A cut of the same kind from the same upload**: one whose preset starts with the same word
   (`Instrumental`, `Vocal`, `Drums`), or another kept source. Its analysis has to be Live's (not
   a sidecar crateside wrote), current for its own file, and the same length to the frame. For a
   recut instrumental, that cut is usually the one lending the warp.
3. **crateside's detector**, when neither exists. In practice that means a first-pass stem Live
   has never opened.

The log names which source each borrowed warp's transients came from.

**Vocal detection is parked.** A vocal now gets crateside's transients only when Live has never
seen any cut of it, and there they serve only as snap points. What was measured is recorded on
#54 for whoever returns to it: stereo does not explain the gap; pitch onsets are 7% of Live's vocal
transients and 9% of the misses; most misses are clear onsets marked about 36 ms early, partly on
fricatives, where Live is itself inconsistent and the user places the warp by ear.

**ADR 0043's reason for shipping lending switched off no longer holds.** It stays off until the
user has checked a borrowed warp in Live (`G - crateside borrows a warp` in the ASD spike folder).

## Consequences

- **A recut looks in Live as its predecessor did**, with the same snap points the user saw before.
- **The detector's vocal shortfall reaches the user only on first-pass vocals**, and there only in
  the clip view.
- **A same-kind cut is not the same sound.** A cleaner instrumental lacks some of the bleed the
  old one had, so a few transients may mark sound that is now gone. That costs a stray snap point
  at most.
