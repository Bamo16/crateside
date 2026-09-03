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

### Confirmed on a real track — 2026-08-18

The above predicted ~150–200k a track. Muse — *Time is Running Out* was then judged with the
exact shipped invocation, prompt included:

| | tokens | API calls | tool calls |
|---|---|---|---|
| track 1 — obscure, needed a segment re-run | 520,645 | 12 | 11 |
| track 2 — well known | 364,653 | 9 | 11 |
| **Muse, after this change** | **156,671** | **6** | **5** |

**−57% against track 2**, which is the like-for-like comparison: both well known, neither
needing a segment re-run. Against track 1, −70%, but part of that is the git attempts this
change removes and part is that Muse is an easier track.

The tool calls are now exactly the minimum the work requires — `Skill`, `WebSearch` ×2,
`Write`, `Bash` — with `permission_denials: []`. No `ToolSearch`, no evidence `Read`, no git.
The two-search cap in `SKILL.md` held, which track 2 had exceeded.

The verdict was A minor / HIGH, sourced to a Hooktheory TheoryTab, with a musickeyfinder
dissent recorded as `algorithmic` and not weighted, and a tonic check arguing Am from the
cadences and explaining the Am/A flicker by the raised 7th. **Nothing was traded for the
saving.**

At this rate the 44-track backlog is ~6.9M rather than the ~13M the doubled numbers implied —
still batched work across days, but no longer undrainable.

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

## What that costs in quota — 2026-09-02

Tokens are the honest unit for comparing two invocations; they are not the unit the user feels.
Twelve tracks were judged in two batches while watching the session meter:

| | tracks | session usage | per track |
|---|---|---|---|
| first batch — uncertain start (~0–1%), one unrelated Sonnet question | 7 | → 46% | 6.4–6.6% |
| second batch — clean endpoints, judging only | 5 | 46% → 73% | **5.4%** |

The second is the better measurement. Separation costs no Claude quota, so both are judging alone.

**A five-hour window is roughly 15–18 tracks** on this account (`subscriptionType: pro`,
`rateLimitTier: default_claude_ai`).

This corroborates rather than repeats the token figure above: at 156,671 tokens a track, 5.4%
implies a window of about 2.9M tokens and 6.5% about 2.4M. Two measurements taken different
ways, agreeing on the shape. The earlier number was not an artifact of how the transcript was
read.

### The meter is readable, and worth reading

`GET https://api.anthropic.com/api/oauth/usage`, with `Authorization: Bearer <accessToken>` and
`anthropic-beta: oauth-2025-04-20`. The token is in `~/.claude/.credentials.json` — plaintext on
Windows, so no keychain step, unlike the macOS tool this was taken from. `five_hour.utilization`
is the number; `five_hour.resets_at` says when it stops mattering. Verified working on
2026-09-02.

**It is undocumented, and the shape is already drifting.** The tool this was learned from parses
`percent`; the live response calls it `utilization` and carries `percent` only in a parallel
`limits[]` array. Half the top-level keys are internal codenames (`tangelo`, `iguana_necktie`,
`omelette_promotional`). So: read `five_hour` defensively, treat an unreadable response as
"unknown" rather than an error, and never let the display depend on a field being there.

**The token is a live credential.** Read it at the moment of the call, never log it, never put
it in an error message shown in the UI, and never send it anywhere but Anthropic.

### A third sample, and why it is an upper bound — 2026-09-03

Twelve tracks judged in a single run, reported by the user from the meter: **17% → 81%**. Sixty-
four points over twelve tracks is **5.3% a track** — the largest single sample so far, and it
lands on the second batch's 5.4% rather than the first's 6.4–6.6%.

**It is confounded, and the main error has a known direction.** A Claude Code session was working
in this repository throughout, on the same account and therefore inside the same five-hour
window. Whatever that session spent is inside those sixty-four points. So 5.3% is an **upper
bound** on what a verdict costs and not an estimate of one; the true figure is lower by however
much the session used, which was not measured.

A second and smaller unknown pushes the other way: the window rolls, so usage from before the 17%
reading may have aged out mid-run, which would make the delta understate what was actually spent.
Neither effect was quantified, and the two do not cancel in any principled way.

**Kept anyway, because the agreement is the useful part.** Three samples, taken on three days, two
of them confounded differently, all landing between 5.3% and 6.6%. That is a stable enough shape
to keep planning against **15–18 tracks a window**. What it is not is a figure to optimise
against — the clean version of this measurement is a batch judged with nothing else running on the
account, and that is what a future pass at reducing token usage should start by taking.

There is a robust alternative worth knowing, because it needs no credential and cannot break:
every verdict run already returns exact `modelUsage` and `total_cost_usd` in its result JSON, so
crateside can account for its own spend precisely. What that cannot know is the denominator, or
what the user spent in their own interactive sessions — which is exactly why the meter is worth
reading despite being undocumented.
