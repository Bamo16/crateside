# A library file can be deleted, and a re-cut keeps the saved warp

Date: 2026-11-17. Issue #157 (staging and library guards). Brief 26. Refines ADR 0014 (the
library's files are the user's) and ADR 0046 (kept warp guides).

## Context

Re-cutting a library track meant moving its stems to Staging by hand, and re-cutting *Perfume!*
replaced its saved warp with a starter: `StemRecut.Apply` recycled the stems and their sidecars
before the new source existed, so `HandoffSidecars` found no sibling holding a warp to lend.

## Decision

1. **No "Bring back to Staging" action.** The brief's proposal is dropped.
2. **A library delete is allowed.** A re-cut, or a source swap, can drop a library file; it goes to
   the Recycle Bin with its sidecar, and the confirmation names the Live sets that use it
   (`ISetLinks.Using`). A library line starts ticked and goes only when the user unticks it. While a
   cut is in flight, library lines cannot be toggled.
3. **The saved warp survives a re-cut.** Before a stem is recycled, `KeptWarps` copies its sidecar
   to `KeptWarpsFolder\<trackKey>\<preset>.flac.asd`. `HandoffSidecars` lends it when no sibling on
   disk holds a warp, and `HandoffDrainer` clears the copies once the handoff succeeds.
4. **New stems go straight to the library** (`LibraryHandoff`) when all hold: every stem of the
   track is warped by the user and in the library, none is in Staging, the new stem took a lent
   warp, and the stems are cut from the track's own upload. Otherwise they go to Staging.
5. **The drums warp guide.** A swapped source replaces every stem, so the recipe asks for a fresh
   guide and all new stems go to Staging. The rule that a guide comes back when a track returns to
   Staging is gone; ADR 0046's kept guides stay as the archive they were.

## Not built

An added source does not yet get its own drums guide. Cutting one needs a separation work item keyed
by the added source, which the pipeline does not have (separation is per playlist track).

## Consequences

A library stem whose warp crateside lent, or one in a library subfolder, does not count as warped
for the direct handoff, so its track goes to Staging. That is the safe side.
