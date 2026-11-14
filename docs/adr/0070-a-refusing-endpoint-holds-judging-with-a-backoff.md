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
  Latest activity ("held · … · until …", clamped to two lines with the whole entry in a tooltip), now always with a time.

## Consequences

- An outage of hours costs about two sessions an hour once the backoff reaches 30 minutes.
- The refusal count and the hold are kept in `%APPDATA%\crateside\paused.json` (`PauseStore`), so a
  restart does not reset the waits. On 2026-10-31 a restart in the middle of a 4-minute hold made
  the next refusal wait a minute again, so the backoff looked as if it never grew.
- Claude Code's own retries inside one session are untouched; crateside decides only when the
  next session starts.
- ~~A user's pause and resume clears the shown hold but not the backoff, so judging still waits it out.~~ Superseded by the amendment below.

## Amendment, 2026-11-08 (#181)

From 2026-11-03 the endpoint refused every session for days. **Measured** in `jobs.jsonl`: the
judge retried at 30-minute intervals all night, about 30 refusals, while the Track page said only
"waiting to be judged". The reason on record was "refused its key", which sent the maintainer to
check an API key that was fine; the endpoint's own error read "DeepSeek rejected every saved
login. Log in and connect again." (the account was suspended). The 30-minute hold also outlived
the fix, because only a served session cleared it.

- **Five refusals in a row, with no served session between, pause judging by itself**
  (`JudgeBackoff.PauseAfter`), as a spent Claude allowance does. The waits before it are 1, 2, 4, 8
  and 16 minutes, so crateside gives up after about half an hour. The Dashboard's Judging card says
  so ("Paused by crateside after 5 refusals in a row: <reason>. Resume to try again."). A pause
  made this way is not stored as the user's: the count in `paused.json` is, so a restart comes up
  paused while the count stands. The spend-down stops on the same count.
- **The user's resume is the only way out.** Resume clears the hold and the count, here and in
  `paused.json`, so after fixing the cause the next pass tries at once. This replaces the old
  consequence that a pause and resume left the backoff standing.
- **The reason carries the endpoint's own words.** A rejected login is "the judge's endpoint
  refused the session: <what the endpoint said>", read from the `result` of Claude Code's JSON
  (lead-in "Failed to authenticate." and "API Error: 401" removed, and the `message` of a JSON error
  body taken out). "Refused its key" is said only when those words mention a key. A 401 or 403
  that Claude Code did not word as "Failed to authenticate" is classified the same way. Nothing
  here knows any bridge or provider by name.
- **A refusal is shown on the track.** A track whose last verdict attempt was refused (a `Held`
  job that stated a wait) reads "judge refused" on its chip, and its Next line, the Track page's
  Judge step and its confidence note read "judge refused: <reason> · next try 4:15 PM", or
  "· judging paused" once judging is paused. It is still never a strike, and never in the ledger.
