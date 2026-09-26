# 0029 — an idle window is opened by one verdict

- Status: Accepted
- Date: 2026-09-24
- Settles [#46](https://github.com/Bamo16/crateside/issues/46). Leaves ADR 0024's pausing untouched.

## Context

A five-hour Claude window only starts when something is spent. One that nobody opens is quota
that expires unused. The user wants the windows kept rolling: when a window has ended and nothing
has opened the next, one cheap job should open it, leaving the rest for real work.

The verdict stage is the only thing crateside does that spends quota, and it starts paused
(ADR 0024) because launching the app must never itself be the act that spends it.

The usage endpoint `QuotaReader` reads is undocumented. Read on 2026-09-24, it reports the
current window as `five_hour: { utilization, resets_at }`. An entry with nothing spent reads
`utilization: 0.0` (other entries in the same payload have exactly that shape with no reset time).
The `limits` list also carries the session window, with an `is_active` flag.

## Decision

**While judging is paused and the user has turned rolling on, crateside judges exactly one track
when the window has read 0% for fifteen minutes, and then not again for five hours.**

- **Off at every start**, like the stage itself. Turned on from the Claude quota cell on Tracks.
- **Only while judging is paused.** A resumed stage is already judging whatever there is.
- **0% is the only reading that counts as idle.** Any spend restarts the clock, and an unreadable
  window never counts, because a roll then might add to a window the user is working in.
- **Fifteen minutes of 0%** before a window counts as idle rather than just turned over. This is
  **guessed**, not measured. It trades a quarter of an hour of each idle window for not racing the
  user opening a session at the rollover.
- **One roll per five hours**, even when the verdict spent nothing (a session that failed to
  start). Without that, a failing agent would be retried every check through the whole window.
- **The roll goes through the verdict drainer**, which now admits one pass at a time. A roll and
  the ordinary stage can never judge concurrently.

## Consequences

- A roll judges whatever is **next in the verdict queue**. It does nothing for the 90 library
  uploads of [#47](https://github.com/Bamo16/crateside/issues/47): those have never been
  downloaded or detected, and nothing yet puts them in front of the verdict stage without also
  queueing a re-cut.
- If the endpoint turns out to report an idle window some other way (no `five_hour` at all, say),
  rolling will quietly never fire. The first real idle window is the test. Check the log line
  `Opened an idle window` after one.

## Amendment, 2026-09-24 — the 90 can be reached after all

*Consequences* above says a roll does nothing for #47's 90 library uploads, because "nothing yet
puts them in front of the verdict stage without also queueing a re-cut". That was wrong when it was
written. `TrackAdd` already judged a pasted library-only upload without cutting it
(`JudgesWhatIsHere`), because the inventory satisfies the plan.

What was actually missing is now built. The Library page queues every never-judged upload at once,
and records each one's audio as accepted (#31), since the library already uses those files. The
janitor sweeps a judge-only track's working set once it is judged, where before it only ever swept
after a separation. Queued, the 90 are fetched and detected straight away, about 44 MB of audio
each (the mean of the nine working sets on disk that day), and a roll judges them one per idle
window.

## Amendment, 2026-09-25 — the backfill only, paused or not

What a roll judges and when it may run are replaced by [ADR 0035](0035-the-library-backfill-is-judged-only-in-idle-windows.md):
an idle window judges only the library backfill, whether judging is paused or not, and the toggle
moved to Settings.
