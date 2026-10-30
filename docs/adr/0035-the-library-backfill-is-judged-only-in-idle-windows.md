# 0035 — the library backfill is judged only in idle windows

- Status: Accepted
- Date: 2026-09-25
- Amends [ADR 0029](0029-an-idle-window-is-opened-by-one-verdict.md). Settles [#64](https://github.com/Bamo16/crateside/issues/64).

## Context

ADR 0029's amendment had the Library page queue every never-judged library upload, to be judged
one per idle window. That did not hold. On 2026-09-25 the user resumed judging, and the ordinary
verdict stage judged the queued library tracks back to back, which was not what they meant. They
paused judging at 18:27 local.

What the user wants: these spend some quota eventually so they go through the proper process,
balanced against how much Claude the user has left to use. Keeping the windows rolling is a
convenient side effect, not the purpose.

## Decision

**A library backfill track is judged only in an idle window, never by the ordinary verdict
stage, whether judging is paused or running.**

- **Marked when queued.** *Queue them for judging* writes `backfill` on the track's instruction in
  `queue.json`. A flag rather than `IsLibraryOnly`, because a queued track stops being library-only
  once it is downloaded.
- **The ordinary stage skips it**, unless it is the track the user bumped with *Work this first*.
  Asking for one track directly is consent to spend on it, and the rest keep waiting (settled
  with the user).
- **An idle window judges only the backfill.** With none waiting, it judges nothing, and ordinary
  tracks wait for judging to be resumed (settled with the user). This replaces ADR 0029's "a roll
  judges whatever is next in the verdict queue".
- **Paused or not.** ADR 0029 rolled only while judging was paused, because a running stage was
  already judging everything. The running stage now skips the backfill, so the guard goes. The
  idle test itself is unchanged: 0% for fifteen minutes, then five hours.
- **The toggle lives in Settings**, remembered across restarts in `paused.json` like the pauses,
  and worded as judging the library backfill one track per idle Claude window. The button on the
  Tracks quota cell is gone.
- The Settings section counts the backfill waiting, judged, and given up, and the unjudged library
  tracks not yet queued. The last should be 0 once *Queue them for judging* has run.

## Consequences

- The tracks queued before this change carry no mark, so they are marked once by a script run with
  the app stopped. It takes every track added by URL with its own upload accepted: 89 on
  2026-09-25, against #47's 90. That pair is *inferred*, not proven, to mean backfill. *Use it
  anyway* on a pasted track writes it too, and `actions.jsonl` (kept only from 2026-09-25) records
  no such case among the 89.
- A backfill verdict can disagree with the key the user tagged by hand, and crateside never
  writes to Mashup Samples. The first was 1991 – *Jungle (Colin Hennerz Remix)*, judged D♯ major
  against a hand tag of G♯ major. The row shows the audit note, and
  `The_index_and_the_ledger_never_disagree_about_a_key` fails until the user settles it.
- The last roll's time is still held in memory only, so a restart forgets it, and a second roll
  can come sooner than five hours after the first. Unchanged from ADR 0029.

## Amendment, 2026-09-26 — the last roll survives a restart

The last bullet above no longer holds. When an idle window judges a backfill track, the time is
kept in `paused.json` (`BackfillJudgedAt`) and read back at start, so a restart keeps the five
hours. Done unasked, overnight, because restarts are frequent and each one could otherwise spend a
second verdict early.

## Amendment, 2026-10-23 — superseded by the weekly spend-down

The idle-window roll is gone. The backlog is judged in the hours before the weekly reset, until
weekly use reaches a ceiling ([ADR 0063](0063-the-backlog-is-judged-in-a-weekly-spend-down.md)).
