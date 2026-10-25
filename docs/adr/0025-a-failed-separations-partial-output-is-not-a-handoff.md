# 0025 — a failed separation's partial output is not a handoff

- Status: Accepted
- Date: 2026-09-23
- Narrows [ADR 0014](0014-the-stems-root-is-transient-and-staging-is-the-handoff.md): the handoff
  reads the cut root, but no longer treats everything in it as finished.
- **Amended 2026-09-23**, below: the self-heal this ADR claimed did not exist yet. See
  *A failure with strikes left is owed*.

## Context

`Moe Shop - HARDCODED` reached `Sample Staging` with a vocal stem and nothing else. Its plan asked
for three. The separation had been killed by a VS Code restart mid-run.

The job history says exactly what happened, to the millisecond:

```
19:13:11.738  Separation  Running    pid 25220
19:19:15.920  Separation  Failed     separation process 25220 is gone without recording a result
19:19:15.995  Handoff     Running
19:19:16.037  Handoff     Succeeded
19:21:16.003  Tagging     Running
```

Orphan detection worked. `SeparationInterlock.Reconcile` found the dead process on startup and
marked the run failed and retryable, which is what it is for. **Seventy-five milliseconds later the
handoff moved the one finished stem into staging anyway.**

`HandoffSelector` enumerates the cut root, groups by the video ID in each file's provenance, and
asks `StageEligibility.Allows` — which only ever consults *the same stage's* history. Nothing in the
handoff's decision looked at separation at all.

Moving the file is what made the loss permanent. `ProcessedTracks` counts a track as fully processed
on "a ledger verdict and **a** stem carrying that video ID on disk" — singular — so one stem in
staging dropped the track out of `Outstanding`, and the retry the interlock had just made it
eligible for never came.

The same race exists during a *healthy* separation: stems are written to the cut root one at a time,
minutes apart, and the handoff may take the first before the rest exist. That case self-heals,
because `HandoffSelector` passes `retrySucceeded: true` and moves the remainder on a later pass.
Only a separation that never finishes turns it into loss.

## Decision

**The handoff skips a track whose separation is recorded `Failed`.**

The stems then stay in the cut root, which is not one of the roots `ProcessedTracks` scans — so the
track keeps a verdict and no stems, lands in `VerdictWithoutStems`, and that list is
`AwaitingSeparation`, whose own summary is "items with a verdict and no stems on disk: the
separation work list". The retry produces the rest and the handoff moves a complete set. The
recovery machinery was already there; the premature move was all that defeated it.

**Only `Failed` blocks.** The two near alternatives were both considered and rejected:

- **Blocking on `Running` as well** would be wrong today. `SeparationInterlock.Release` is called
  only from inside the `SeparationRunner.RunAsync` that started the process, so an *adopted* run —
  one that outlived a restart — is never released and its job stays `Running` for ever. Blocking on
  `Running` would maroon that track's stems permanently. That this has never happened is measured:
  across 202 recorded separations, none has a final status of `Running` or `Queued`. It is a reading
  of the code, not an observed failure, and it is filed rather than fixed here.
- **Requiring the recipe's full count** to be present is the more complete answer, and it is the one
  that would also catch a separation that is still running. It needs the plan and the recipe, which
  the selector does not take, and it raises what "complete" means for a track whose stems the user
  has deliberately deleted. Deferred with the issue below.

`Canceled` does not block either: a re-cut records it to reopen the stage, and nothing is in the
cut root at that point for the handoff to take.

## Consequences

- A separation that exhausts its three strikes leaves its partial output in the cut root and the
  track sitting in the separation work list making no progress. That is a visible dead end instead
  of a silent one, and **Retry** forgives the failure and starts it moving again.
- The healthy mid-separation race is untouched. It still happens and still self-heals; this ADR only
  stops the case where the remainder is never coming.
- Not fixed, and filed: "a stem" satisfying fully-processed, and the adopted run that is never
  released. Either can still make a partial set permanent by another route.

## Amendment, 2026-09-23 — a failure with strikes left is owed

The decision above says the retry "produces the rest and the handoff moves a complete set", and
that **was wrong for the very track it was written about.** Tested rather than reasoned about, once
the user reported that the fix left them with no way forward.

`Moe Shop - HARDCODED` was reachable from nowhere:

| way in | why it was shut |
|---|---|
| `Outstanding` | fully processed — a verdict and *a* stem in staging |
| `verdictWithoutStems` → `AwaitingSeparation` | it has a stem, so not this either |
| `AwaitingCut` → `AwaitingSeparation` | `ReopenedTracks.IsOwed` was `Canceled or Queued or Running`. **`Failed` was not owed.** |
| re-cut in the picker | the plan already asks for `Instrumental - Full`, so `AsksTheSameAs` holds and no confirm is offered |
| the **Retry** button | shown only when `GaveUpAt is not null`, which needs the strikes exhausted. It had one of three. |

`consumeOnArrival` is on, so the playlist had let the track go the moment crateside took it on. That
matters: a track still *in* the playlist retries through `verdictWithoutStems`, which is why this
went unnoticed. Once consumed, the only route back is as a reopened item, and `IsOwed` refused it.

The state had happened before and gone unnoticed: `4PffwRpiw-A` has sat on a failed separation since
2026-08-16. It stays inert even now, because `QueueService.Reopened` builds an item only for a video
ID the ledger knows and that one was never judged.

**So `IsOwed` now counts a `Failed` stage whose strikes have not run out.** A failure is work still
owed; that is what strikes *mean*. Having given up is not owed, deliberately — it would sit in the
line for a GPU at a position it never advances from (#32), and by then Retry is on screen because
the strikes ran out. The sequence reads, end to end: fail → retry up to the limit → give up → Retry.

`StageProgress.Limit` became `StageEligibility.MaxStrikesFor` so the two places that need a stage's
strike budget share one, rather than the new caller adding a third copy.

A failed *handoff* is owed too and correctly does not reach the GPU: `AwaitingSeparation` filters
reopened items through `AwaitingCut`, which is narrowed to `JobStage.Separation`.

### What this does not fix

The dead end had five shut doors and this opens one. The other four are still shut, and two of them
are wrong on their own terms — a re-cut that offers nothing when the plan already asks for a missing
file, and a Retry button that appears only after a track has given up. Both are in
[#49](https://github.com/Bamo16/crateside/issues/49).
