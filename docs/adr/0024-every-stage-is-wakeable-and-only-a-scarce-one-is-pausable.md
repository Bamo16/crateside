# 0024 — every stage is wakeable, and only a scarce one is pausable

- Status: Accepted
- Date: 2026-09-23
- Settles the three questions [#48](https://github.com/Bamo16/crateside/issues/48) left open, and
  corrects one premise in that issue's own suggested fix.

## Context

`StageWorker` took an optional `DrainControl`, and only the Verdict stage was given one. Download,
Evidence, Handoff and Tagging fell through to `Task.Delay(IdleDelay)` and always slept the full two
minutes, with nothing able to cut the wait short.

So a plan change the user had just made waited out an interval per stage. Ticking **Source** on an
imported track produces the file by copying it out of the retained working set — measured at 0
seconds — and then moves it into staging. Two stages, each sleeping up to two minutes, for a copy
and a move.

`DrainControl.Wake()` already existed for exactly this and was reachable only by the one stage
nobody needed to wake.

## Decision

**Pausing and waking are different things, and they are now different types.**
`DrainSignal` is the wake: a one-permit semaphore and the wait that it cuts short. `DrainControl`
derives from it and adds pausing and activity reporting.

Every stage gets a signal; only a stage that contends for something scarce gets a control. That is
the same distinction `JobStage` already draws — pausing exists because two separations at once cost
the user their machine and a verdict run costs quota, and for a file copy there is nothing to pick
a moment for.

`StageSignals` holds one signal per `JobStage` and is what both worker loops wait on. Separation
and Verdict are handed their existing `DrainControl` and `VerdictControl` rather than a fresh
signal, because a wake has to reach the object that worker is already waiting on.

**A stage that did work wakes the others.** Not the stage after it — *the others*. The pipeline is
not a line: a plan that keeps the source has Download write into `Key Identified`, which is
precisely the directory `HandoffSelector` enumerates, skipping Evidence, Verdict and Separation
entirely. A successor map ordered by `JobStage` would have gone Download → Evidence and left the
reported four-minute wait exactly as it was.

Only a *successful* pass broadcasts. A failure made nothing for anyone downstream.

**A user action wakes the stages it concerns**, from `Tracks.razor`: a plan change wakes Download
and Separation, adding a source wakes Download. Not a blunt wake-everything — see below.

## Why not wake everything

The issue proposed that a blunt wake-all "costs a tick of each loop against an idle queue, which
may be the right trade". That premise does not hold, and it is the one thing here worth stating
plainly.

A tick of Download, Evidence, Verdict or Separation begins with `QueueService.ReadAsync`, which is
`ScanAsync` — a ledger read plus two `ProcessedTracks.Scan` walks that open every audio file under
three roots. The code already says so of itself: *"the most expensive thing in the app … must not
run per event."* Handoff and Tagging are the cheap ones; they read a directory and the job store.

This is established by reading the call path, not by timing it. The number is not needed, because
the decision turns on the ratio: waking four scanning stages where two suffice is strictly worse
whatever the scan costs.

Two things keep the broadcast honest despite that:

- Verdict and Separation check `IsPaused` before reading the queue, and both start paused. A wake
  they do not want costs one boolean.
- A stage that is finding work is already on `BusyDelay`, five seconds — so during a busy period
  the scan is being paid every five seconds regardless. The broadcast adds at most one.

## Consequences

- `StageWorker`'s control parameter is gone; the five registrations are identical but for the
  drainer, behind one `Worker<TDrainer>` factory.
- The playlist read is cached for two minutes, the same as `IdleDelay`, so an early wake usually
  serves the queue from cache and does not spend API quota. Waking cannot add reads beyond moving
  them earlier.
- Pausing is unchanged in every respect, and the existing `DrainControl` tests pass untouched
  because `Wake` and `WaitForWorkAsync` are inherited.
- Not addressed: `IdleDelay` is still two minutes. Nothing here argues it is the right number —
  only that a change the user made should not have to wait for it.
