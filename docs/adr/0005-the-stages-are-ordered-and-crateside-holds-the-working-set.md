# 0005 — the stages are ordered, and crateside holds the working set

- Status: Accepted
- Date: 2026-08-16

## Context

[ADR 0001](0001-crateside-orchestrates-the-workflow.md) named three stages — **evidence
run**, **verdict run**, **separation** — and built only the third. `SeparationDrainer`
exists; nothing produces evidence and nothing produces a verdict. So crateside orchestrates
the one stage that needs an agent least and cannot run the two that come before it.

The gap is not cosmetic. On 2026-08-16 the drainer separated a track titled *"Eat Sleep
Dance (feat. Moe Shop) **Instrumental**"* with `--preset vocal_full --preset
instrumental_full`, producing a vocal stem of a track with no vocals. It could not have
known better: `SeparationRecipe` is fixed at `Standard`, and the ledger held no line for a
track that had never been detected.

The obvious reading — *evidence has to run before separation so the recipe has something to
read* — is half right in a way that changes the design. **An evidence run produces no ledger
line at all.** `record_result.py` requires an agent layer and `validate_agent_layer` requires
a verdict, so a track that has only been detected is indistinguishable from one never
touched. Both of separation's ledger-derived inputs are therefore still absent after
detection:

- the **recipe**, via `has_vocals` — and [ADR 0004](0004-sample-is-the-role-word-and-stem-is-the-origin-word.md)
  already fixed the policy for absent: *"crateside's policy is to expect the usual vocal and
  instrumental pair."* Eat Sleep Dance was that stated policy correctly applied to a track
  nobody had judged.
- the **destination**, via `HasTaggableKey`. No verdict routes to `Unresolved Key` — and
  **nothing moves stems afterwards.** There is no re-route path anywhere in
  `Crateside.Core`. Every track separated ahead of its verdict lands in `Unresolved Key`
  permanently, even once a HIGH verdict arrives, so the library's own record of what is safe
  to pair silently goes wrong.

ADR 0001 §3 scheduled the stages independently *against the resource each needs*. That
independence of **scheduling** was read as independence of **ordering**.

## Decision

**1. Separation is gated on a recorded verdict, not on evidence.** The selector skips any
track with no ledger line. One gate fixes the recipe and the destination together and means
no re-route path has to exist. This does not reintroduce the coupling ADR 0001 broke: that
complaint was that judgment and separation spent Claude quota and GPU *together, in one
unit*. A dependency is not a coupling — verdict runs happen whenever quota is idle,
separation drains whatever has a verdict, and neither waits on the other's resource.

A WITHHELD or LOW verdict is still a line, so it gates through and routes to
`Unresolved Key`, which is exactly the case that folder exists for. `WorkOrder`'s "no verdict
at all" branch becomes unreachable and should say so.

**2. The deterministic core is promoted out of the skill.** ADR 0001 §6 permitted crateside
to drive keytag's *declared CLI*, and everyone read that as invoking
`run_key_detection.py` — which lives at `.claude/skills/detect-key/scripts/`, inside the
skill that same ADR plans to hollow out, and pins itself there with a `parents[4]` path
computation. Both callers want the same core and only one of them is a skill. keytag
promotes it to a declared entry point ([keytag ADR 0006](../../../keytag/docs/adr/0006-the-detector-is-a-second-declared-interface.md));
crateside invokes that name and never a `.claude` path.

**3. crateside holds a working set, and keytag never learns it exists.** Splitting evidence
from verdict *in time* creates an artifact that has nowhere to live: today the machine layer
persists only inside a finished run record, because the agent produces both halves in one
session. crateside keeps it, keyed by video ID, under `%LOCALAPPDATA%\crateside`.

An evidence store inside keytag would have exactly one writer, and that writer is crateside —
which *is* keytag knowing crateside exists, the thing ADR 0003 (keytag) and ADR 0001 §4 both
work to prevent. A file under crateside's own state directory is job state in precisely the
sense `TrackJob` already is.

Nothing has to change in keytag's recorder to accept it: `record_result.py --machine <path>`
already takes an arbitrary path, so the verdict run points it at crateside's file and the
resulting ledger line is identical to today's.

**4. One download per track, and it must be StemForge's.** The evidence run acquires the
audio with `stemforge-cli download`; detection reads it; separation is handed the **local
path** rather than the URL.

This survives only because of a StemForge behaviour worth writing down, since crateside's own
ported comment said the opposite: `AudioTagger.ReadSourceTagInfo` parses provenance out of a
local input's `COMMENT` and carries `SourceUrl` forward into the stems it writes — its doc
names the case, *"download once, then separate on the saved path"*. So the **source video ID
survives**, and the video ID is the join key for the Staging board, for *fully processed*, and
for the queue's self-emptying delete.

**The condition is load-bearing.** The retained file carries provenance only because
`stemforge-cli download` tagged it. A file acquired any other way — yt-dlp directly, a
hand-placed file — has no `stemforge/` comment, `ParseProvenance` returns all-null, and every
stem loses its join key. The evidence run is therefore **not free to choose its own
downloader**, for a reason that has nothing to do with downloading.

**5. The working set is discarded at import, on one rule.** It holds the source audio *and*
the evidence, and both go together when the track is imported. The evidence JSON is a few
kilobytes, so keeping it past the ledger line costs nothing and buys a single rule instead of
two clocks.

The deletion predicate is **"separation succeeded, and nothing carrying this video ID remains
in `Sample Staging`"**. Both halves are required: "nothing in staging" is also true of a track
that has never been separated, so the naive form would delete the source of every track before
its stems existed. And it must be *nothing*, not *no samples* — the manual workflow empties
staging progressively, so a partially imported song must not trip it.

Tying it to the **import signal** ([the manual workflow](../the-manual-workflow.md)) means the
source stays available through exactly the window when the user is working the track by hand,
which is when they would discover the key is wrong or a stem needs redoing. The same fact that
says a song is finished says its source is disposable.

The window is long — keytag's glossary is explicit that a track *"can sit here for weeks"* —
so at ~10–15 tracks a batch and ~30–40 MB a flac this is a few hundred MB steady-state. It
wants a visible size and a manual purge, not invisibility.

**6. The recipe is a plan; the instrumental release is a fact.** ADR 0004 chose to record
`has_vocals` as *"the **fact** rather than the plan ('separate drums only'), so it still
explains itself when the workflow changes, and crateside derives the plan from it."* That
still holds, and it means the two are separate fields rather than one:

- **Instrumental release** — the fact. Stated by the user in the Queue view, beside a hold,
  keyed by video ID. It changes the *default* recipe to drums-only with a kept source, and it
  flows through the work order to the verdict run so the ledger still records `has_vocals`
  exactly as designed.
- **Recipe override** — the plan. crateside's alone, overriding the default for any track for
  any reason, never reaching keytag. Not built now; the seam is: recipe resolution becomes a
  function of the queue entry rather than a constant, so adding it later is one field and a
  picker.

Once the queue holds a plan, the fact stops being recoverable — a drums-only recipe *suggests*
an instrumental release, but so does a user who wanted only drums. `vocal_clean` instead of
`vocal_full` is the case that proves they are different, since it is a pure preference with no
fact behind it at all.

**7. The fact travels crateside → keytag, reversing ADR 0004's implied direction.**
`has_vocals` flowed ledger → crateside because the skill was the only thing that ever talked
to the user. That is now backwards: **crateside has a screen and keytag does not**, and the
fact is a property of the release, known the moment the track is queued rather than discovered
by detection. In the pipeline as designed there is otherwise *no moment where a user is asked* —
evidence is unattended and the verdict run is a headless session with nobody present.

**8. A loop per stage; one pause; the pause states no reason.** Head-of-line blocking is
enough on its own: one sequential loop means a paused GPU stops detection of everything queued
behind it, and detection costs nothing. Failure isolation is the second reason — a stage
failing on one track must not stall other tracks in other stages.

Only separation carries a pause. Evidence contends for nothing, which is the premise the whole
three-stage split rests on. Claude quota is genuinely tight during working sessions, but a
verdict run is now cheap enough — evidence in, verdict out, no download and no separation —
that it does not earn a control; ADR 0001's reasoning stands, the consequence it implied does
not, and this is recorded so nobody re-derives one from it.

The implementation is not three of anything: `DrainWorker` is thin and every decision already
lives in a drainer plus a selector, so this is one worker parameterised by stage and three
selectors.

**9. A verdict run succeeded iff a ledger line exists for the video ID.** The same idiom
[keytag ADR 0003](../../../keytag/docs/adr/0003-a-separate-blazor-app-owns-the-human-half.md)
chose for queue removal, for the reason it gave: *"idempotent and checks reality rather than a
claim, so partial failure needs no special handling."* It is the exact negation of the stage's
own selection predicate, so a session that died halfway leaves no line and is naturally
retried. The exit code is kept for the error message and the strike count and decides nothing,
which also means crateside never parses agent output and the one-way dependency survives
intact.

This makes `source_url` load-bearing in a way it was not before — see keytag ADR 0006, which
makes it required.

## Alternatives considered

**A headless Claude Code session for the evidence run.** No keytag change at all. Rejected:
it spends Claude quota on the one stage ADR 0001 identifies as needing none, which undoes the
split that ADR exists to make, and turns a deterministic stage non-deterministic.

**Declaring `run_key_detection.py` stable where it sits.** The smallest change today, and
literally what ADR 0001 §6 says. Rejected because it points crateside's dependency at
`.claude/skills/detect-key/`, a directory whose contents are on their way out by the same
ADR's plan.

**Evidence-only lines in `runs.jsonl`.** One store, one idiom, nothing new to build. Rejected
as actively unsafe rather than merely impure: lookup is latest-line-wins per song identity and
crateside reads `HasTaggableKey` off that line, so re-running evidence on a track that already
has a verdict would append a verdict-less line and hide the good one. The same class of bug as
the `-- segment reanalysis` identity split, which has happened here once already.

**Two lifetimes for the working set** — evidence deleted when the ledger line lands (it is
duplicated into `record["machine"]` at that point), audio deleted at import. Each artifact
dies the moment it is genuinely redundant. Rejected for two clocks over a few kilobytes.

**Separation keeps taking the URL, accepting a second download.** Rejected once StemForge's
provenance pass-through was verified; the fear that drove it — that a local-file job loses the
source URL — was true at one point and had been fixed, and crateside's ported comment was a
snapshot read as a fact.

**`--keep-source` as the fix for downloading twice.** This was the framing carried into this
work, and it dies with the ordering change: the evidence run already has the file, so there is
nothing to keep. `--keep-source` survives for one job only — the instrumental release, where
the source track *is* the sample and has to reach the destination folder. Whether it copies a
*local* input into `--output` is unverified and should be measured, not assumed.

**Detecting instrumental releases by title.** Would have caught Eat Sleep Dance exactly, since
its title said so. Rejected as an unmeasured heuristic, which is the failure mode this project
keeps hitting — and the manual workflow already records that *official instrumentals* and
*instrumental releases* are different things not yet modelled, with 2 instances in 435 files,
so a title match would conflate them.

**Building the recipe override now.** Rejected as designing a picker before there is a case
that needs it; the seam costs nothing and the picker can wait for a real `vocal_clean`.

**Three symmetric pause controls.** Rejected: consistency is not a reason, and the only stage
with an observed reason to stop is the one that spins the GPU.

## Consequences

- **crateside now depends on a keytag interface that does not exist yet.** keytag ADR 0006
  has to land first; crateside cannot be built against it until it does.
- **The `--segment` consequence ADR 0001 left open is resolved cheaply.** Its dilemma —
  *"either crateside exposes a 're-run with this window' call, or one narrow execution path
  stays in the skill"* — was sharp only while the detectors were skill-internal. Now that the
  runner is a declared name, "stays in the skill" is the skill calling a public entry point on
  a file it was handed. The work order carries both the evidence and the audio path.
- **What gets recorded when there are two machine layers is parked**, not solved.
  `record_result.py --machine` takes one file, and a dilution case is a known-hard example
  whose whole-file failure is the interesting half for tuning. Revisit if segment re-runs get
  common enough to skew the corpus. *(This supersedes the old trigger, "needs the skill's
  final shape first" — the shape is settled.)*
- **`StemProvenance`'s "Absent for local-file jobs" comment is stale** and must be corrected;
  it is a port-time snapshot of StemForge that was read as a fact about it.
- **The working set is the second non-derived thing crateside holds**, after the job store —
  but unlike the job store it is scratch, not a record. Losing it costs a re-run, never a
  fact, and it should be safe to delete by hand at any time.
- **`SeparationRecipe` gains a variant** and stops being a constant. The instrumental-release
  recipe is drums-only with a kept source; `c6e115d` taught the *board* to recognise a kept
  source but nothing yet asks for one.
- **The Queue view gains a second per-track instruction** beside holds. `queue.json`'s stated
  rule — *"a standing instruction about what to work, not a record that anything ran"* —
  already covers it.
