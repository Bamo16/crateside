# 0048 — stems arrive in staging already warped, at crateside's own tempo

- Status: Accepted
- Date: 2026-10-02
- Amends [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md). Builds
  [#77](https://github.com/Bamo16/crateside/issues/77).

## Context

ADR 0047 gives a starter warp keytag's `bpm.value`, and writes starter and borrowed warps in a
once-a-minute pass over staging. The user raised two problems with that on 2026-10-02:

- **The tempo.** A starter warp is meant to capture what crateside worked out about the track, and
  an outside figure can only make it worse. On the songs with a finished warp, crateside's own
  beatdetect reading of the instrumental (hpss, v1.1.0) matched the warped tempo exactly on 387 of
  492, and within an octave on 478. keytag's online-led recommendation matched on 258 of 405, and
  was outright wrong on 22 (Counts, 2026-10-02).
- **The wait.** A stem in staging without a sidecar might need the user's warp, or might be waiting
  for crateside's analysis, and the user cannot tell which. Previewing it in Live meanwhile makes
  Live write a sidecar first. The reading itself takes seconds; the wait came from the beat worker
  looking for unread instrumentals only every ten minutes.

## Decision

**At handoff, every stem's sidecar is worked out in the working set, before anything moves.** That
is a lent warp when a cut of the upload holds the user's warp, otherwise a starter. Each sidecar is
written the moment its stem lands in staging, and recorded at its staged path. The track's row says
it is still moving stems into Sample Staging until that is done.

**A starter's tempo is crateside's own beatdetect reading** of the batch's instrumental (then the
kept source, then karaoke), taken during the handoff and kept for the beat worker under the staged
path. keytag's figure is not used. A reading that drifts gives no starter, because one tempo would
misrepresent the track. A batch with no instrumental-like cut gets none either.

**The once-a-minute pass stays** for what happens later: lending once the user saves a sibling's
warp, and stems placed in staging by hand. It reads the tempo the same way, from the beat worker's
kept readings.

Both remain switched off (`Warps:LendAutomatically`, `Warps:WriteStarterWarps`) until the user has
checked a borrowed warp in Live.

## Consequences

- **With starters on, a handoff takes longer**, by one beatdetect run on the instrumental (the whole
  file, then each 30 s of it) and one decode per stem, all on the CPU.
- **Separation order and drum stems do not matter for the tempo.** It is read from the instrumental
  with hpss, which matched the AI drum stem on 2026-09-30 (#83).
- **What keytag's verdict does with BPM is now keytag's question** (keytag#2): crateside no longer
  needs it for anything it writes.
