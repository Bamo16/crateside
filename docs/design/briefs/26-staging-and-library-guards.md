# crateside: which operations stay staging-only, and bringing a track back

Issue: #157 (review where crateside allows an operation only in staging, and decide which belong in
the library too)

For an implementing agent; Bamo decides from the table below, and implementation follows in
separate issues. Decisions behind the guards: ADR 0003 (tag store), 0014 (the library's files are
the user's), 0039/0040 (rename in the library, relinking sets), 0046 (kept warp guides), 0052
(library key correction), 0059 (loudness).

## Problem

Bamo, in the issue: re-cutting a library track's stems (fusq – *Perfume!*, re-cut as source only)
"meant moving its stems back into staging first: the library is still treated as a protected area.
The maintainer leans towards allowing more operations in the library." He also saw two things to
explain: **the warp was lost** (the stems had a saved warp; after the re-cut the source arrived
with a starter warp), and **the drums stem came back to staging**; his preference is that when a
track returns to staging to be warped again, its drums stem comes back with it if crateside has one.

## Current behavior: every guard

| operation | where the guard is | what it protects | verdict |
|---|---|---|---|
| Re-cut (change a plan) | `Orchestration/Edits/StemRecut.cs:35` reads only `staged.Of`; library files count as "already exists" but are never removed (:38-44) | library files are in Bamo's Live sets by path; a delete leaves them missing | keep removal staging-only; allow the **additive** case in place (below) |
| Recipe change | the same `PlanFor`; a re-cut that only adds (`RecutPlan.IsAdditive`) removes nothing | nothing lost | allow in the library |
| Source swap | `Orchestration/Edits/TrackSwap.cs:45` removes `staged.Of`; `TrackEdits.razor:111-116` says "crateside never writes there"; `TrackRow.SamplesLeftInLibrary` (:55) | same as re-cut | keep; bring back first |
| Superseded stems | `Orchestration/Handoff/SupersededStems.cs` scans staging only (ADR 0014) | same | keep |
| Remove / forget | `TrackRow.CanForget` (:265) false when any sample is in the library | a forget would orphan Live's files | keep |
| Retag (key) | `TrackRow.cs:586` treats a moved sample as tagged; ADR 0052 lets crateside correct a library key to the verdict | hand-applied tags | already allowed for the key; keep for genre and the rest |
| Rename | allowed in the library (ADR 0039/0040), with relinking of sets | | already allowed: the precedent |
| Loudness, clip colour, warp-off markers | `SidecarWriter.Level/Recolor/GiveWarpOffMarkers` rewrite library sidecars | | already allowed |
| Re-warp (starter/borrow) | `Warps/HandoffSidecars.cs:28` scans staging and library for siblings but writes only to the stems being handed off | a warp Bamo saved in Live | keep; the borrow already reads the library |
| Beats reading | `BeatWorker.cs:33` reads only samples with `IsInLibrary: false` | cost, not safety | allow: a library sample has no need today; leave |

The line is: **crateside edits a library file's contents or metadata it owns (tags, clip fields,
names with relinking); it does not delete or move a library file without Bamo moving it.**
Everything staging-only in the table is a delete, a move, or a decision that needs one.

## Design

### 1. One action: "Return to Staging" (the recommendation)

Allowing deletes in place would mean crateside rewriting Live sets for a path that vanished, which
ADR 0040's relinker does for a rename but not for a delete. **Recommend instead one action on the
Track details actions, *Bring back to Staging*, that does by crateside what Bamo did by hand:**

- Moves every library sample of the track (audio, `.asd` sidecar, and its tag-store entry) into
  `Sample Staging`, the inverse of `HandoffRunner` (`Orchestration/Handoff/HandoffRunner.cs`).
- Before the move, the track's sets are warned about the way a rename warns (ADR 0039), and the
  relinker (ADR 0040) retargets them to the staging path; a moved file the set cannot find is
  listed. **Confirm:** warn first and relink. Alternative: move without relinking, as Bamo did by
  hand.
- Brings back the drums warp guide too: if the track has a kept guide
  (`CratesidePaths.KeptWarpGuides`, ADR 0046), it is copied into staging beside the others so the
  track warps as a whole group. **Confirm:** only on *Bring back to Staging*. Alternative: also on
  any re-cut of a staged track.
- Afterwards the track is an ordinary staged track, so re-cut, swap, superseded stems and the
  rest work with no change.

### 2. Allow in place only what loses nothing

- **An additive plan change** (a preset added; `RecutPlan.IsAdditive`) runs on a library track
  without bringing it back: new files are cut into staging and joined to the track when it is
  next handed off. Only the removal step stays staging-only, and the plan UI says so. **Confirm:**
  allow additive changes in place. Alternative: always bring back first, one rule.
- Everything else in the table stays as it is.

### 3. The lost warp (fix, no decision)

Suspected cause, from the code: `StemRecut.Apply` recycles each dropped stem **and its sidecar**
(`StemRecut.cs:88-89`, `RecycleBin.Remove(AnalysisFor(...))`) when the plan changes, before the
new source exists. `HandoffSidecars` then finds no sibling holding a warp to borrow
(`SidecarWriter.HasWarpToLend`) and writes a starter warp instead.

- **Step 0, a reproduce-only agent**: re-cut a staged copy of *Perfume!* and read the log
  (`%LOCALAPPDATA%\crateside\logs`) to confirm the order, and record whether the source was taken
  from the local copy (`WillRefetch`, `StemRecut.cs:54`) or downloaded again. No fix in that run.
- **Fix**: before `Apply` recycles anything, read the saved warp from the files about to go and
  keep it in the track's instructions (beside `SetPlan`), and let `HandoffSidecars` use it as the
  authority when no sibling holds one. The warp is lent at the new source's own length and BPM, so
  an upload at a different length falls to a starter as before. The note stream records
  `Re-cut kept the saved warp` or `Re-cut lost the warp: <why>` and `source: local copy |
  downloaded again`.

## Test plan

- `StemRecutTests`: a plan that drops a stem whose sidecar holds a saved warp leaves the warp
  retrievable; an additive plan on a library-only track plans without refusal and removes nothing.
- A `ReturnToStaging` test with a fake library: audio, sidecar and tag-store entry arrive in
  staging; a track with a kept guide gets the drums stem too; a track without one does not;
  a file open in Live reports and stops without partial moves.
- On the library, fusq – *Perfume!*: *Bring back to Staging* moves its stems (and the drums guide),
  Track details then reads "In staging", re-cut as source only keeps the saved warp, the warp view
  shows the warp lane on the saved markers rather than a starter, and the handoff moves everything
  back to the library with the sidecars intact.
- Another library track with a kept guide, not re-cut: after *Bring back to Staging* its drums
  lane appears in the warp view.

## Out of scope

Deleting library files in place; changing the rename, key-correction or loudness rules; relinking
beyond ADR 0040's existing relinker; the starter-warp quality.
