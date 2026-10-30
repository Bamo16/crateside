# 0045 — a recut takes its warp from its own kind first

- Status: Accepted
- Date: 2026-10-02
- Amends [ADR 0043](0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md). Builds
  [#54](https://github.com/Bamo16/crateside/issues/54).

## Context

ADR 0043 lets only an instrumental, karaoke or the kept source lend a warp, because a vocal is
sometimes re-warped on its own. On 2026-10-02 how that happens, and what follows from it, was set
out:

- **A group is warped together, on the drum stem.** The drum stem is then deleted, or left in
  staging (#87), and the instrumental and vocal each carry the warp, usually still in step.
- **A vocal is sometimes shifted 20–100 ms earlier** so that it sits right over another track's
  instrumental. After that, the vocal and the instrumental of one upload hold different warps.
- **For a recut, vocal follows vocal and instrumental follows instrumental.** For a song's tempo
  as ground truth, the instrumental is used (or the drums, when the drums are what is under test).

Under ADR 0043, a recut vocal took the instrumental's warp and lost the shift.

## Decision

**A stem takes its warp from a saved cut of its own kind first**: one from the same upload whose
preset starts with the same word, as in ADR 0044. Only when there is none does it fall back to
ADR 0043's lenders (an instrumental, karaoke or the kept source). The fallback is the first-pass
case: the user has just warped the group, and no vocal of the upload has been saved before.

**A sidecar crateside wrote is not a lender of its kind.** It holds a copied warp, not one the
user made for that kind, and the fallback reaches the original anyway.

Cuts of one kind that hold different warps are reported and nothing is written, as ADR 0043 does
for instrumentals.

## Consequences

- **A shifted vocal keeps its shift through a recut.**
- **Tempo as ground truth is unaffected.** The library already reads the warped tempo from an
  instrumental first, and a vocal's shift moves the offset, not the tempo.
