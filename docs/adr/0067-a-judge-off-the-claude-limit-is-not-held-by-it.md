# 0067 — A judge off the Claude limit is not held by it

- Status: Accepted
- Date: 2026-10-27
- Narrows [ADR 0053](0053-judging-stops-at-85-percent-of-the-weekly-limit.md) and
  [ADR 0063](0063-the-backlog-is-judged-in-a-weekly-spend-down.md) to a judge on Claude Code's own login.

## Context

`Judge:Environment` can point the judge at another Anthropic-compatible endpoint (docs/judge.md).
The weekly hold (ADR 0053) and the spend-down window (ADR 0063) both exist to ration the weekly
Claude limit, which such a judge does not spend. Kept in force, they stop intake judging at 85% of a
limit the judge is not using, and keep the library backfill waiting for a window that rations nothing.

## Decision

**The judge spends the weekly Claude limit only when `Judge:Environment` sets none of
`ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY` and `ANTHROPIC_AUTH_TOKEN`** (`JudgeOptions.SpendsClaudeQuota`).
When it does not:

- **The quota guard never holds.** No hold, no "Judge this one anyway".
- **With Library backfill on, ordinary judging works the backlog:** new intake first, then songs owed
  a re-judge, then the library backfill, the order the spend-down uses. The user's pause still stops
  it, and Library backfill off keeps ordinary judging to intake.

The spend-down itself is unchanged and still judges in its window; with one judgment at a time, the
two never overlap.

## Consequences

- Pacing and any limits of the other endpoint are its own business; crateside judges one track at a
  time and adds no rate limit of its own.
- The ledger does not record which endpoint judged a run; the date the configuration changed is the
  only record.
