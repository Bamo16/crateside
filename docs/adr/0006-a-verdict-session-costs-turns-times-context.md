# 0006 — a verdict session costs turns times context

- Status: Accepted
- Date: 2026-08-18

## Context

The pipeline runs end to end and produces real verdicts. The judgment is good: on one track
the session found that CREMA had failed over the whole file, re-ran `keytag-detect` over a
window it chose from bar-count structure rather than from flicker confidence, got three-way
agreement, and logged Beatport's dissent as `algorithmic` without weighting it. Nothing below
is a complaint about what the session *does*.

What it costs was the open question, and the first answer was wrong.

### The first measurement counted the same tokens several times

Two sessions were read out of `~/.claude/projects/v--Repos-keytag/<session-id>.jsonl` by
summing `message.usage` over every row that had one. That reported **1,143,527 and 986,031
tokens**, and those numbers went into a handoff as the reason the 44-track backlog was not
drainable.

A row in that file is a **content block**, not an API call. One assistant response carrying a
thinking block, a sentence and a tool call is three rows — and all three carry the *same*
`usage` object, because it describes the response. Deduplicating on `requestId`, which is
identical across the rows of one response:

| | rows with usage | **API calls** | reported total | **actual total** |
|---|---|---|---|---|
| track 1 (Burst Mode) | 27 | **12** | 1,143,527 | **520,645** |
| track 2 (Furious Angels) | 25 | **9** | 986,031 | **364,653** |

So a track costs **~365–520k**, not ~1M. Notionally $0.44 and $0.54.

This is the fourth time this project has acted on an unchecked premise, and the second time
the unchecked premise was inside a *correction* of an earlier one. The rule in `CLAUDE.md`
already covers it; what this adds is that **a measurement is a premise too**. "I measured it"
was treated as ending the question, and the script was never asked what it was counting.

### Where the money actually goes

Cost is **API calls × context**, and context is dominated by a fixed floor re-read on every
call. Measured with a no-op prompt (`Reply with exactly: OK`) under the invocation as it
stood: **24,866 tokens** before the skill loads, ~30.9k after.

Three things were paying for that floor without earning it, all found by reading the
transcripts rather than reasoning about them:

**1. `--allowed-tools` is a permission list, not an availability list.** This was the
expensive misunderstanding. Naming four tools there does not stop the other ~30 being handed
to the session; it only decides what may *run*. With all of them loaded, the rarely-used ones
sit behind `ToolSearch` — and `WebSearch`, which the skill needs on almost every track, is one
of them. **Both measured tracks spent an entire round trip looking up a tool they had already
been granted.** `--tools` is the flag that decides what exists.

**2. The user's global settings were loaded into a session that cannot use them.** Every
skill, agent and plugin they have installed, listed in full, in a session allowed five tools.

**3. The session read keytag's `CLAUDE.md` as addressed to itself.** *"Commit directly to
`master`… do not ask each time."* Track 1 spent three calls and roughly 150k tokens trying to
commit the ledger. All three were blocked by the permission list — the guard working exactly
as intended — but a blocked attempt costs the same as an allowed one.

And one thing the prompt was making the session fetch for itself: the evidence file, at
2–4 KB, read with a tool call.

## Decision

**1. `--tools` names what loads; `--allowed-tools` keeps naming what may run.** The two lists
are now both present and deliberately different. `Skill,Read,Write,WebSearch,Bash` load;
`Bash` is still narrowed to `Bash(uv run:*)` by the permission list, so the structural guard
against a second separation is unchanged and un-widened.

**2. `--setting-sources project`.** The skill is a project setting of the keytag checkout, so
that source has to stay. `""` was measured rather than assumed: it drops user *and* project
sources, and `/detect-key` then does not resolve at all — the session reports the skill body
as missing and judges nothing.

**3. The evidence is carried in the prompt, not pointed at.** A tool call is a whole round
trip; the file is under a thousand tokens in a prefix that is cached anyway. The path is still
named, because `record_result.py` takes it as its `--machine` argument. Bounded at 32 KB —
not because of any file seen (they are 2–4 KB of fixed shape) but because `key_events` is a
flicker list with no length in its definition, and one pathological track must not turn the
saving back into a cost. Over the bound it falls back to naming the path, which is what it did
before and still works.

**4. The prompt says whose repository the session is standing in.** Naming who keytag's git
instruction is *for* beats forbidding git flatly, because a session that is merely forbidden
can reason its way back to obeying a file it can read.

**5. `--max-budget-usd 2.00` as a runaway backstop.** Roughly four times the worst measured
track. Verified to trip — it reports `errors: ["Reached maximum budget ($…)"]` and stops —
and verified to check *after* a call rather than before the first, so it cannot deadlock a
session that has done nothing. A trip leaves no ledger line, which the runner already reads as
an ordinary failure, which is what a runaway should be.

### What this is measured to buy

The per-call floor, under the exact flag list the runner now emits:

| | floor | |
|---|---|---|
| before | 24,866 | |
| `--tools` | 17,990 | −6,876 |
| `+ --setting-sources project` | 13,292 | −4,698 |
| exact runner flags, re-measured | **13,312** | **−46%** |

`--exclude-dynamic-system-prompt-sections` was measured and rejected: 13,293 against 13,292.
It improves cross-user cache reuse and does not reduce size.

Call counts, from the two transcripts: one `ToolSearch` each, one evidence `Read` each, three
git attempts on track 1. That is 5 of track 1's 12 calls and 2 of track 2's 9.

**This is a projection until a track runs.** Floor and call counts are measured; their product
is not. The order of magnitude is a ~60–65% cut, to roughly 150–200k a track.

## Alternatives considered

**`--bare`.** Skips CLAUDE.md discovery, auto-memory, hooks and plugin sync in one flag, and
would have killed the git problem at the root. Rejected because it also forces authentication
to `ANTHROPIC_API_KEY` and never reads OAuth or the keychain — and the entire reason this ADR
exists is a *subscription allowance*. It would have moved the cost to a credit card.

**Trimming `SKILL.md`.** 20 KB, ~5.2k tokens, carried on every call — the largest single item
left. Rejected for now: it is keytag's to own, the judgment quality it produces is the thing
this pipeline is *for*, and cutting the instructions an agent reasons from to save tokens is
how you get cheap wrong answers. Revisit only with a verdict-quality check to compare against.

**Resuming one conversation across tracks.** Already tried and reverted in `37bf523`. The
shared prefix worth amortising is ~50k paid once per track; the previous track's history is
~90k paid *per turn*. **Do not re-propose without new measurements** — and note that the
measurements that killed it were the double-counted ones, so the effect was real but its
magnitude was overstated like everything else here.

**`--max-turns`.** Named in the handoff as the backstop to add. It is not a CLI flag — it is
an SDK option. `--max-budget-usd` does the same job against the quantity actually being
rationed.

## Consequences

- The verdict stage's cost is now dominated by what the session genuinely reads: the skill,
  the searches, and its own output. The unearned floor is mostly gone.
- **Two lists that look interchangeable are not.** `--tools` and `--allowed-tools` sit
  adjacent in the argument array with a comment saying which is which, because the failure
  mode was silent — everything worked, it just cost double.
- 44 tracks is still batched work across days rather than one drain, and that expectation
  should be set rather than engineered away.
- The transcript-reading script that produced the wrong numbers is not worth keeping; the
  deduplication rule is. **Group `message.usage` by `requestId`.**
