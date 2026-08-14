# 0001 — crateside orchestrates the workflow; the agent is reduced to judgment

- Status: Accepted
- Date: 2026-08-13
- Amended: 2026-08-13, after the GPU-contention premise was measured wrong. See
  **Correction** below; the original reasoning is left in place rather than rewritten.

## Context

`detect-key` (in `keytag`) had accumulated the whole import workflow: choosing the
canonical URL, judging format/premium quality, downloading, running the detectors,
weighing the evidence, searching for corroboration, deciding what to separate,
routing stems to `Key Identified` / `Unresolved Key`, and recording the run. Most of
`SKILL.md`'s length is that workflow logic rather than key detection.

The prompt for this decision was the user's observation that the skill "was beginning
to smell," and that crateside should "take responsibility for as much of the workflow
as possible and dumb down keydetect to... detect keys."

The smell has a sharper diagnosis than "too much prose in one file". The skill welds
together **three kinds of work whose only commonality is being in the same file**, and
which contend for three unrelated scarce resources:

| | work | scarce resource | when it wants to run |
|---|---|---|---|
| **A** | download, run the three detectors | ~nothing (15–40s CPU, network) | any time, unattended |
| **B** | weigh evidence, corroborate, produce the verdict | **Claude quota** | whenever quota is idle |
| **C** | separation | **GPU**, ~6–7 min/track, strictly serial | when the machine is free |

Because A, B and C are one unit today, every run spends GPU and Claude quota together
whether or not either is free. The user's usage pattern makes this actively costly:
quota is tight during coding sessions and idle otherwise, while the GPU is only
available when they have surrendered the machine. These are different schedules.

This split is not new vocabulary — it is keytag's existing **"the code gathers, the
agent concludes"** principle (`CONTEXT.md`, *Evidence and judgment*), which had been
enforced in code but never in *scheduling*.

## Decision

**crateside owns the workflow and pushes work; the agent is invoked as a judgment
function.** Concretely:

**1. crateside decides, and crateside launches.** The canonical URL, the
premium/format judgement, batch selection, stem destination routing and queue cleanup
all move to crateside, which already computes or owns each of them. A "run this batch"
button in the Queue view starts the work.

**2. A human never transports the instruction.** ADR 0003 (keytag) proposed that the
Queue view remove friction "by listing what is new **and producing the prompt**". That
half was never built, and it is now **reversed**: the prompt is still composed by
crateside, but it is handed to a headless Claude Code session rather than pasted by
the user. The insight is that ADR 0003 was right that a prompt is the artifact and
wrong that a human is the transport.

**3. The three stages are scheduled independently, against the resource each needs.**

- **Evidence runs (A)** run whenever, unattended.
- **Verdict runs (B)** are scheduled opportunistically to soak up idle Claude quota.
- **Separation (C)** stays **ad-hoc and button-driven**, exactly as ADR 0003 decided.
  *(Superseded same day — see **Correction**. C now drains continuously.)*

ADR 0003's *"processing stays ad-hoc; scheduling is an explicit v2"* is **not
overturned**. Its entire stated justification is GPU contention — "a batch is ~2 hours
of strictly serialized GPU work that would make the machine unusable if it collided
with the user." That reasoning applies to C and to nothing else. Scheduling B was
never decided against, because at the time B and C were inseparable and the question
could not arise.

**4. The one-way dependency survives.** crateside invokes the skill; the verdict comes
back through `results/runs.jsonl`, which crateside already reads and keytag already
writes. There is no new file format, no API layer and no reversed build dependency.
keytag still does not know crateside exists — the skill simply becomes a function that
takes parameters instead of making decisions.

**5. Shelling out to `stemforge-cli` is correct here**, and does not contradict the
standing preference for managed .NET. That preference targets *reimplementing by
scraping* — `ffprobe` for tags where TagLibSharp exists, `yt-dlp --flat-playlist`
where `playlistItems.list` exists. `stemforge-cli` is first-party, is the only thing
that performs separation, and grew `--json` specifically so other tools could drive
it. Delegating to an executor is not the same act as scraping a foreign binary for
data.

**Consequently the `net10.0` / `net11.0` constraint disappears.** crateside never
project-references `StemForge.Core`, so multi-targeting it was never necessary. That
constraint was load-bearing only under the assumption that crateside would execute
separation in-process.

**6. crateside may drive keytag's declared CLI, not its internals.** crateside may
invoke `run_key_detection.py`, but only on the condition that keytag **documents it as
a stable interface**, held to the same standard as `stemforge-cli`. This is the first
time keytag has to change for crateside's benefit, and it thickens ADR 0003's
deliberately thin boundary from "it reads a file" to "it reads a file and drives a
declared script."

**7. crateside gains persistent job state — the first non-derived state in the
system.** An append-only `%APPDATA%\crateside\jobs.jsonl`, last-line-wins per job id,
mirroring the `runs.jsonl` idiom. It exists because "in progress", "failed at 04:12",
and "cancelled" are **not derivable from the filesystem**, and because the app is
restarted constantly during development.

**8. Separation is single-flight, and orphans are adopted rather than killed.** The
job store records the running PID and start time. On startup crateside checks whether
that process is alive and adopts it. On Windows a child survives its parent, so
without this a restart mid-batch would launch a *second* `stemforge-cli` — and
`SKILL.md` is emphatic that concurrent separations thrash the GPU and make the machine
unusable. Killing the child instead would discard up to two hours of GPU work on every
hot reload, which is the worse trade.

**9. No quota prediction.** There is no supported way to read remaining Claude
allowance; an undocumented OAuth endpoint exists but is the same class of dependency
`docs/premium-outlook.md` already rejected for the YouTube Music counterpart link.
Instead, verdict runs are **idempotent, resumable and cheap to fail**: "judge the next
N unjudged tracks", failing fast when quota is exhausted and resuming later. This
converts an unanswerable prediction problem into a retry problem. Failures are
persisted with a strike count so a poisoned track stops being retried nightly.

## Correction — the GPU-contention premise was never measured

Everything above about *when* separation may run rests on one sentence from ADR 0003:
a batch is *"~2 hours of strictly serialized GPU work **that would make the machine
unusable if it collided with the user**."* That sentence was inherited and restated
here without ever being checked with the person whose machine it is.

Checked, the same day, it is **wrong**. Two facts, from the user:

- **More than one separation at a time brings the system to a standstill.** Absolute;
  never do it.
- **A single separation is essentially unnoticeable.** The GPU is audible and the room
  warms slightly, but music production, coding and streaming all continue normally.

So "two hours of GPU work" and "two hours of unusable machine" had been treated as the
same fact, and are not. The real constraint is **concurrency, not contention** — and
concurrency is exactly what `SeparationInterlock` already enforces. It turns out to be
the only constraint rather than one of two.

**Consequences of the correction:**

- **Separation drains continuously and is on by default.** Requiring the user to pick a
  moment was solving a problem that does not exist. The queue empties itself, one track
  at a time, gated only by the interlock — the same shape as verdict runs, so the worker
  is one loop rather than a scheduler plus a button.
- **A pause control is kept, for a different reason.** Unnoticeable is not silent: with a
  live microphone, fan noise is a real reason to stop for an hour. That is an occasional
  judgement, not a gate on every batch.
- **One track per invocation, not one batch.** `separate` does accept many inputs and
  loop them serially, but batching made "the GPU is busy" and "this track is being
  worked" two different facts sharing one PID. One track per process costs some repeated
  start-up and buys dramatically simpler state, plus a pause that responds at track
  boundaries instead of batch boundaries.
- **StemForge's warm long-running Python process is deliberately not adopted.** It exists
  in the GUI to avoid exactly the per-invocation start-up cost above, and pulling it in
  would overcomplicate this for a saving that does not matter when nobody is waiting.
- **ADR 0003's v2-scheduling decision is now genuinely superseded**, rather than
  side-stepped as the original text of this ADR claimed. Its conclusion was reasonable
  given its premise; the premise was simply never verified.

The lesson is one this project keeps re-learning and is worth writing down: **a settled
decision inherits the truth of its premise, and a premise nobody measured is not
settled — it is only old.**

## Alternatives considered

**Keep the human in the loop — crateside emits a prompt to copy/paste.** ADR 0003's
original design. Rejected by the user directly: "it would be silly for the human to
copy/paste something into claude code." It also leaves crateside unable to act on its
own decisions, which is the entire point of the change.

**The agent pulls: the skill asks crateside what to do next over HTTP.** crateside is
already a local web server, so this is nearly free, and it avoids persistent job
state. Rejected because it keeps the *trigger* human (a session still has to be
started by hand), spends ADR 0003's explicit "no API layer, no JSON contract" virtue,
and reverses the dependency so that keytag's skill must know crateside exists.

**crateside writes a work-order file that the agent reads on session start.** Weakest
coupling and no API. Rejected as paste-a-blob wearing a hat — something still has to
tell the agent to go and look.

**Scheduling separation as well as judgment.** Originally rejected on the grounds that
ADR 0003's GPU-contention argument had not weakened, and that a scheduled 2-hour job
must know the user is absent. **Both grounds fell with the premise** — see
**Correction**. A continuous drainer need not know whether the user is present, because
their presence does not conflict with it. This is now the chosen design.

**Cowork routines for the scheduled half.** Attractive because routines draw on a
separate daily allowance the user is not otherwise spending. Rejected on a hard
constraint: **routines execute remotely**, and every input and output of this workflow
is a local file — the GPU, `P:\RYAN\Ableton\*`, the keytag ledger, `localhost`.
Bridging that would mean exposing a deliberately local personal tool to the network.
The premise also self-defeats: routines exist to run while the machine sleeps, but
evidence production requires the machine awake.

**In-memory job state.** Simpler, and preserves "nothing is persisted but the derived
view". Rejected because the app restarts on every save under `dotnet watch`, so
in-flight state would be lost several times an hour during development — and because
in-memory state cannot prevent the double-launch in point 8.

**SQLite for the job store.** The reflexive choice for mutable job state. Rejected at
this size: tens of records, a single writer, no querying beyond "what is open". JSONL
matches an idiom already in the system and stays inspectable with a text editor.

**crateside owning detection outright**, reducing keytag to a ledger. Rejected — it
discards the premise that keytag is the detection tool, for no gain.

## Consequences

- **`SKILL.md` shrinks to judgment**: weigh the evidence, corroborate, record. It
  loses URL choice, format judgement, download, separation, routing and queue
  management. The destination is a skill that receives evidence and returns a verdict.
- **This is staged, not done in one step.** The orchestrator (job store, background
  service, interlock, headless invocation, button) is identical either way and is
  built first; the skill initially keeps running the detectors, and that step moves
  into crateside once the plumbing is trusted.
- **Verdict runs become much cheaper in tokens** — no download, no file management, no
  separation orchestration, just evidence in and verdict out. This is a real quota
  saving, but it is a saving in the *normal* allowance, not access to a new one.
- **keytag must now declare `run_key_detection.py` a stable interface.** The one-way
  dependency survives but stops being free: keytag has changed for nobody until now.
- **`--segment` re-analysis is judgment-driven**, so an agent that concludes the
  whole-file read was diluted cannot simply re-run it. Either crateside exposes a
  "re-run with this window" call, or one narrow execution path stays in the skill.
  Unresolved; decide when the skill is reduced.
- **crateside acquires a background service and a scheduler**, which it did not have.
  The Blazor request path never owns long work.
- **The Staging board stays purely derived.** Job state covers *runs*, never the
  library. Nothing about a track's real progress is read from the job store — verdicts
  still come from the ledger and completion still means leaving `Sample Staging`.
- **keytag ADR 0003 needs amending** to record that its prompt-production decision has
  been superseded, and that its "no API layer" virtue survived.
