# 0063 — the backlog is judged in a weekly spend-down

- Status: Accepted
- Date: 2026-10-23
- Supersedes the idle-window roll of [ADR 0029](0029-an-idle-window-is-opened-by-one-verdict.md)
  and [ADR 0035](0035-the-library-backfill-is-judged-only-in-idle-windows.md), and amends
  [ADR 0053](0053-judging-stops-at-85-percent-of-the-weekly-limit.md) for the backlog.

## Context

The backlog (songs owed a re-judge, then the library backfill) was judged one track per idle
five-hour window: 0% for fifteen minutes, then five hours off. That spends at most a few tracks
a day, and nothing at all on a day the user works in every window. The quota that actually goes
to waste is the weekly remainder at the reset.

The usage endpoint reports the week beside the five-hour window. **Read on 2026-10-23:**
`seven_day: { utilization: 42.0, resets_at: "2026-10-28T09:00:00.155738+00:00" }`, with
`five_hour` of the same shape; the `weekly_all` row of `limits` repeats it as `percent: 42`.
Utilization came in whole percents on that reading.

## Decision

**In a window ending at the weekly reset the usage source reports, crateside judges the backlog
one track after another until weekly use reaches a ceiling.** Outside the window the backlog is
not judged.

- **The reset is read, never configured.** The window is the last *N* hours before
  `seven_day.resets_at`. No reading, or one whose reset has passed, opens nothing.
- **Order as before:** new intake (unless judging is paused), then a re-judge, then the backfill.
- **The ceiling replaces the 85% weekly hold for these tracks.** Spending to near the limit just
  before the reset costs the user nothing they could still have used.
- **The five-hour window still counts.** No track starts while it reads at or above its own
  ceiling, so the spend-down leaves room for new intake and the user.
- **Settings, all three:** window 6 hours, weekly ceiling 98%, five-hour ceiling 90%. All
  **chosen**, not measured. Six hours always holds one whole five-hour window whatever time of day
  the reset falls, without reaching far into a user's last waking day of the week. 98% leaves a
  track's worth of slack. At about 5.3% of a five-hour window per track
  ([ADR 0006](0006-a-verdict-session-costs-turns-times-context.md)), 90% leaves room for one
  more.
- **The on/off switch keeps its place** (`backfill` in `paused.json`, so it survives a restart);
  the three settings are kept in `spend-down.json`.
- **Measured as it runs.** Each track logs weekly use before and after at Information
  (`Spend-down judged …`), since tracks per weekly percent has never been measured.

## Consequences

- The idle-window roll, its fifteen-minute test and `backfillJudgedAt` are gone.
- A failed track waits five minutes before the next, so an agent that cannot start is not
  retried in a loop through the night.
- With whole-percent readings, a single track's delta is often 0 or 1; the rate comes from a
  night's lines together, not from one.

## Amendment, 2026-10-23 — spend all of it, as late as possible

The maintainer's aim is to spend every drop of quota that would otherwise expire at the reset, as
late as possible so it does not compete with the user. The **Settings** line above is changed:

- **Window 3 hours** (was 6), weekly ceiling **100%** (was 98%), five-hour ceiling **100%** (was
  90%). All still **chosen**, still settings, so another user can hold some back. Quota unused at
  the reset is lost, so a track the usage limit cuts off partway costs nothing that would
  otherwise have been kept; the spend-down therefore keeps starting tracks until the usage source
  refuses (a ceiling of 100% is reached at a reading of 100).
- **A cut-off is harmless, and checked.** When the limit stops the claude session, the runner
  finds `api_error_status` 429 or "hit your limit" in its output (the check ADR 0055's runner
  already made for the ordinary stage) and returns *agent unavailable* before it looks at any
  withheld verdict, so nothing reaches the ledger. The job is recorded `Cancelled`, which carries
  no strike, and the song stays owed: a re-judge leaves the queue only when the ledger holds a
  run from its cutoff on, and a backfill track stays outstanding until the ledger has a run for
  it. Covered by `VerdictRunnerTests`, including a limit hit on the resume after a withheld
  verdict.
- **The spend-down holds itself after a cut-off.** The ordinary stage pauses judging on
  *agent unavailable*; the spend-down does not (that would also stop intake after the window), and
  instead takes no new track until the five-hour window's reset the last reading reported (an hour
  if it gave none), then resumes if the weekly reset is still ahead. Without this a cut-off would
  be retried every five minutes with the reading still at its ceiling. The hold also covers a
  weekly limit hit before the reading shows 100%: one cheap failed attempt per five-hour window.
- Not measured: how the CLI words the limit message beyond the two phrases above, and how far the
  usage reading lags the real limit.
