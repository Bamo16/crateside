# 0070 — A refusing endpoint holds judging with a backoff

- Status: Accepted
- Date: 2026-10-30
- Amends [ADR 0067](0067-a-judge-off-the-claude-limit-is-not-held-by-it.md) and
  [ADR 0069](0069-a-verdict-is-checked-against-its-evidence-before-it-is-recorded.md): how long
  judging holds after the endpoint refuses, and which refusals hold.

## Context

On 2026-10-30 the judge's endpoint (an Anthropic-compatible bridge) started answering 429 while judging
one song, Snail's House – *Sunday* (`hUR2OJIRbqA`). **Measured** in that day's log and `jobs.jsonl`:
from 16:28 to 16:44 local, 36 judge sessions were started for it, often 2 to 12 seconds
apart. Each was classified as a rate limit and recorded as `Held`, with the endpoint's stated wait (69 s,
then 900 s) as `heldUntil`. The hold was not kept: the verdict stage's worker waits by a signal that
any other stage's success also releases (ADR 0060), and the next pass started a session without
looking at the hold. A busy pipeline (downloads, evidence, tagging) ended the wait every few
seconds. ADR 0069 had noted that another stage's wake "can retry sooner", but not what that costs
against an endpoint already refusing.

Only a 429, an unreachable endpoint, a refused key and a missing login held judging. An overloaded
endpoint (529), a 5xx, or a timeout failed the track instead, a strike against a song that had done
nothing wrong, and each was resumed once for a second session before failing.

## Decision

- **A session the endpoint refused holds judging** (`VerdictResult.IsRefused`): a 429 (other than a
  spent Claude allowance), an overload (529 or "overloaded"), any 5xx, an unreachable endpoint, a
  refused key, and any other `API Error` result except a 400, 413 or 422, which are about the
  request and so about the track. This holds whatever the endpoint is, Claude's own included. It is
  read from Claude Code's result JSON and stderr only, never from a bridge's own status page.
- **The hold is the stage's, and every pass checks it** (`JudgeBackoff`, owned by `VerdictControl`),
  so a wake from another stage finds it and starts no session.
- **How long:** the wait the endpoint stated, where it stated one (still read from "pause for N
  seconds", capped at an hour); otherwise one minute, doubling to 2, 4, 8 and 16, then 30 minutes
  for every refusal after. A stated wait counts as a step, so an unstated one after it waits longer.
  A session the endpoint served, whether or not it produced a verdict, starts the steps again.
- **A refused session is not resumed.** The run ends at the refusal, so one attempt is one session.
- **A refusal never counts against the song.** Its job is `Held`, which carries no strike, never
  `Failed`.
- The spend-down sees the same hold, and a judgment started from a track's page records a refusal
  in it too, without pausing judging.
- A spent Claude allowance, and a missing login on Claude's own login, still pause judging
  (ADR 0067).
- The hold is shown where it already was: the Judging card ("Held: … Trying again at …") and
  Latest activity ("judging held · … · until …"), now always with a time.

## Consequences

- An outage of hours costs about two sessions an hour once the backoff reaches 30 minutes.
- The refusal count and the hold are kept in `%APPDATA%\crateside\paused.json` (`PauseStore`), so a
  restart does not reset the waits. On 2026-10-31 a restart in the middle of a 4-minute hold made
  the next refusal wait a minute again, so the backoff looked as if it never grew.
- Claude Code's own retries inside one session are untouched; crateside decides only when the
  next session starts.
- A user's pause and resume clears the shown hold but not the backoff, so judging still waits it out.
