# 0009 — a track has one state, and the page is one list

- Status: Accepted
- Date: 2026-09-07

## Context

Before the Tracks page, **crateside did not model where a track had got to.** The words existed —
queued, judging, separating, ready to warp, parked, held — but only as descriptions of *which
bucket a track happened to fall into*. That was spread across three places: `QueueView`'s five
work lists, the `JobStore`'s per-stage records, and which root a track's files were sitting under.

Each of those is right for its own reader. A stage asks "what may I pick up", and a work list is
the correct answer. The problem was that the **user interface had no other way to ask the
question**, so it rendered the buckets: five sections, and a track's position on screen determined
by which list it was currently in.

That scattering is the root of every friction the redesign was asked to fix. Its sharpest form:

> hold/unhold or a track changing between the different in-progress or waiting stages resulted in
> tracks being bucketed into categories which were each displayed in different sections and that
> jumping was frustrating/annoying when attempting to interact with them. Especially when I'm the
> one causing the change like with hold/unhold.

A row's **address changed every time its meaning did**, and the worst case was the user's own
action moving the row out from under the press.

## Decision

**A track has one derived state, and the page is one list narrowed by filters.**

`TrackState` is that state: one value per track, resolved in a fixed precedence order from the
ledger, the job store, the user's standing instructions, and what is on disk. `TrackRow` is the
join that produces it. Nothing writes a state down and nothing is marked done by hand — it is
read, like everything else in this codebase.

Two consequences follow directly, and they are the point:

**State becomes a column rather than an address.** A row stays where it is and its chip changes.
The filter pills narrow one list instead of addressing five, so a track that stops matching
disappears from the view the user chose rather than reappearing somewhere they have to go and find.

**The copy cannot drift from what it describes.** The chip label, its colour, and the `Next`
sentence are all derived from the same `TrackState` on the same record. The design supplied them
as a table; deriving them in one place is what stops that table becoming three tables that
disagree.

## Consequences

**Rows do move, at exactly three boundaries.** Grouping the list turned out to be worth having —
done tracks belong at the bottom, and ready-to-warp tracks want to be together and sorted by name
so they can be scanned beside Ableton's browser, which sorts the same way. So the promise is not
"rows never move" but the narrower thing the complaint was actually about: a row moves only on
reaching *ready to warp*, being parked for want of a key, or being finished with. Queued through
tagging is one group, so crossing the pipeline moves nothing — and **held is in that group too**,
which is the case that mattered most, since a hold is a change the user makes.

**Two views of the same tracks now exist, and both are correct.** `QueueView` sorts the playlist
into what each stage may pick up. `TracksView` puts it back together as one row per track. They
answer different questions — "what may I work" and "where is my track" — and collapsing them would
break one of the two.

**A row is not a playlist entry, and that has been learned three times.** A track leaves the
playlist as soon as it is fully processed while its files sit in `Sample Staging` for weeks, so
rows are composed from four sources: the playlist, the staging board, re-cuts in flight, and the
library. Each was added after something keyed on playlist membership was found to be silently
wrong for most of the tracks the user cares about — no row at all (`b601b61`), instructions
reading as unset (`40c170e`), and work that no stage could pick up (`71db24a`, [ADR 0010](0010-the-playlist-is-the-inbox-not-the-work-list.md)).

**The state is derived, so it can be wrong in one place only.** Every bug in it has been a missing
input rather than a stale value, which is the failure mode worth having.
