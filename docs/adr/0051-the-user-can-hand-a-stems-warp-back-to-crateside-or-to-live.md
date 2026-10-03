# 0051 — the user can hand a stem's warp back to crateside or to Live

- Status: Accepted
- Date: 2026-10-02
- Builds on [ADR 0043](0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md) and
  [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md). Design: handoff 14 (#97).

## Context

Track details now draws each stem's warp from its sidecar (handoff 14). Two things the user could
only do in Live, or not at all, come with it:

- **A lent warp the user then changed** cannot be put back: crateside never writes over a saved
  clip of the user's (ADR 0037), and once Live saves over a borrowed warp it is the user's.
- **Live's own analysis cannot be had back** once crateside has written a sidecar. A sidecar with
  crateside's transients, or none, gives Live nothing to re-detect from; Live analyses a file only
  when it has no sidecar.

## Decision

Two controls on the selected stem, each behind a confirm:

- **Re-apply crateside's warp** writes the agreed sibling's warp over what the stem holds, as
  lending would, **including a warp the user saved**. This is the one write over a user's warp, and
  only when the user asks for it on that stem. The old sidecar is copied to
  `%APPDATA%\crateside\backups\warps\` first. It is offered only when a sibling holds a warp of the
  user's and this stem's differs from it.
- **Let Live analyse** copies the sidecar to the same backups, sends it to the Recycle Bin, and
  records the stem in `live-analyses.json`. Until Live writes a fresh sidecar (the next time the
  stem is previewed or loaded), lending skips the stem. The warp worker then puts the old clip and
  warp back into Live's fresh file, keeping Live's transients, and records it as crateside's again
  if it was a borrowed or starter warp. If the user saves a warp in Live first, the stem is
  dropped from the record and nothing is put back.

The warp worker checks for fresh sidecars once a minute even with lending and starter warps
switched off.

## Consequences

- **Every sidecar crateside replaces or recycles at the user's request is kept** under the
  backups folder, never pruned.
- **A stem waiting for Live has no warp**, so the board counts it unwarped until Live opens it.
- **Neither control touches siblings.**
