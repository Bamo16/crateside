# 0015 — hold is removed, and the playlist is the way out

- Status: Accepted
- Date: 2026-09-10
- Answers [#11](https://github.com/Bamo16/crateside/issues/11).
- Follows from [ADR 0010](0010-the-playlist-is-the-inbox-not-the-work-list.md), which changed
  what the playlist means and therefore what hold was for.

## Context

Hold existed because the playlist used to be a pile of things saved to come back to — *"tag this
so I remember to download, separate and key-detect it by hand"*. Clearing that backlog meant
going through each entry deciding whether it was good to go, should be removed, or needed
replacing with a proper music URL. Holding everything and releasing each one as it was checked is
exactly what that job needed.

The playlist now means *"run the workflow for this, now"*. Nothing is added until the right music
URL has been tracked down, so the verify-this-entry step hold existed to pace is gone. The user
had not used it since.

## What was weighed

**The GPU argument, which sounded strongest, was the weakest.** `QueueView.Outstanding` excluded
held tracks and said so in strong terms — *"the drainer reads this collection directly, so a held
track cannot reach the GPU by any path"*. But the user's answer was *"if I don't want the GPU
running, I should pause separation, which has been working well"*. Pause is global and
reversible; hold is per-track and sticky, and the user reaches for the former. The guard was
defending against something another control already handles better.

**Ordering was not a reason.** FIFO has been fine. The one ordering wish is to bump a single item
to the top, and that is a better use of the space hold's button occupies than hold is.

**Stem selection did not survive inspection.** Holding a track to choose presets before
separation assumes the choice is knowable up front, and it is not: *"I'm never gonna know the
exact stems I want until I've first tried the instrumental full / vocal full presets."* That is a
re-cut, which already exists and needs no hold.

**The automatic case, raised last, is not hold at all.** A track whose upload yields no premium
audio is worth stopping for review. But that is crateside reporting a finding, not the user
issuing a standing instruction — you *clear* a hold and you *resolve* a finding, and they have
different lifecycles. `TrackState.Parked` already means "crateside stopped, your turn". Making
`IsHeld` mean both "the user said so" and "the machine said so" is the conflation
[ADR 0014](0014-the-song-is-the-identity-and-the-name-is-not.md) had just finished undoing for
names.

## Decision

**Hold is removed.** No state, filter, chip, store method or persisted field survives.

The way to stop crateside working a track is to pause a stage, or to take the track out of the
playlist. That is a real reduction in what can be expressed — hold was the one structural way to
say *leave this alone* without deleting anything — and it is accepted, because the thing it
expressed is no longer a thing the workflow needs said.

**An old `queue.json` still reads.** `held` and `holdReason` are simply unknown properties now,
and unknown properties are skipped rather than refused, so the rest of the file survives and the
next write drops them. The older top-level `held` array that ADR 0005 preserved a migration for
reads the same way: the file loads, and it contributes no instructions.

## Consequences

**21 tests went with it**, and the suite is smaller rather than weaker: every one of them
asserted hold behavior. Several store tests were kept and re-vehicled onto the
instrumental-release fact, because they were never about holding — they were about an instruction
surviving a restart, a second field not being dropped by saving the first, and an instruction
that says nothing not being kept. Those rules still matter.

**The `Next` sentence for a track that gave up changes.** It said *"retry, or hold it"*, which was
the row offering the control this removes. It now says *"retry, or take it out of the playlist"*.

**Two ADRs are amended in place** rather than left contradicting the code — 0005, where the
instrumental-release fact was described as sitting "beside a hold", and 0009, where held was
named as the case that mattered most for rows not moving. 0009's reasoning is worth keeping: hold
mattered there *because* it was a change the user makes to a row they are looking at, which is
exactly the shape of the bump-to-top control that replaces it.

**Bump-to-top takes the space.** Wanted on its own merits, and it is the thing the user actually
reaches for in the one case hold was still nominally covering.

## What this does not decide

The premium-audio stop is worth building and is its own work. Two things about it were settled
here only to the extent of ruling hold out as its mechanism:

- The trigger must be the **measured** shortfall that `stemforge-cli download --json` already
  reports and crateside currently discards — not the API's art-track marker, which fires on 226
  of 552 corpus videos and is needless for 94 of them. Measured 2026-09-10: `dQw4w9WgXcQ` carries
  no marker and yields premium audio anyway.
- It must stop **before the verdict**, because a swap copies the verdict forward
  ([keytag ADR 0008](../../../keytag/docs/adr/0008-a-restatement-is-not-a-run.md)) — so a key
  derived from poor audio would be restated onto the better URL rather than re-derived.
