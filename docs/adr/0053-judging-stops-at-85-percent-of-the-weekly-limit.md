# 0053 — judging stops at 85% of the weekly limit

- Status: Accepted
- Date: 2026-10-13

## Context

crateside read only the five-hour window. On 2026-10-03 the weekly limit stood at 90% with four
days to the reset, and judging (about 37k tokens of session start-up per track) would have kept
running into it, blocking every other Claude use until the reset. The usage payload carries the
weekly figure (`seven_day`, or the `weekly_all` row of `limits`) beside the five-hour one.

## Decision

- Judging, normal and library backfill alike, does not start a track while weekly use is at or
  above 85%. A backfill that follows the user's backfill switch still follows this guard, so quota
  is never spent where the maintainer is not expecting it.
- It is a **guard, not the user's pause**. It is derived from the last reading, never written to
  `paused.json`, and lifts by itself when a later reading drops below 85% or the weekly reset time
  in the payload passes, even if the reading cannot be refreshed.
- A reading that fails leaves the previous one standing; it never lifts a hold.
- The board says why judging stopped and when it resumes, in plain text on the Judging card.
  The guard has no visual state of its own yet.
- The 85% is a constant (`QuotaGuard.WeeklyLimit`). Further guards, such as stopping when the
  weekly figure is trending to exhaust before the reset, are wanted but not yet defined; they
  would be settings, since a user may choose to spend the quota anyway.

## Addendum, 2026-10-13: an unread quota holds judging

After a restart the usage endpoint answered 429, no reading had succeeded yet, and the guard
(holding only a failed `Unknown`) judged past a 94% week. Until the first successful reading
the guard now holds, saying the quota has not been read. A reading that fails after a good one
still leaves that one standing. The per-track pass can let a track through an unread hold.
