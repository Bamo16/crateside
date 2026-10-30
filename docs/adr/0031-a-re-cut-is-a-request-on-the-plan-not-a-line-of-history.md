# 0031 — a re-cut is a request on the plan, not a line of history

- Status: Accepted
- Date: 2026-09-24
- Settles [#50](https://github.com/Bamo16/crateside/issues/50), Option A, with the user's
  go-ahead on its three questions.

## Context

A re-cut and a source swap used to write `Canceled` into job history for separation, handoff and
tagging, and five readers inferred what the user wanted from those lines. `Canceled` meant four
things (two requests, two outcomes), and a history line could not say *what* was asked for, so a
source-only re-cut had to be kept from reopening separation by hand (19412fb). Five of the six bugs
tabled on #50 sat on those history reads.

## Decision

**A re-cut or swap records `cutRequestedAt` on the track's instruction in `queue.json`, and writes
nothing into job history.** Readers compare that time with the outcomes after it:

- **A separation is owed** while nothing has succeeded since the request and none is running. A
  failure since the request is left to the strikes. A track that had given up before the request is
  asked again, as a `Canceled` line used to do.
- **A request is settled** once a handoff has succeeded since it. A cut and a kept-source copy both
  end in one, so this answers a copy-only re-cut too, which never separates.
- **Readers:** the separation list (`awaitingCut`) and the separation selector read *owed*. The
  kept-source download, the janitor and row reachability read *settled*. The audio download treats a
  track as swept up only if nothing is owed since its last separation.
- **A swap is the same request.** The new URL is already on the instruction as `sourceUrl`, so no
  second field is needed.
- **A satisfied request is not cleared.** A newer outcome makes it old, so `queue.json` gets one
  write per request and none on completion.

`Canceled` now only ever means *ended without fault* (a stale tag write, an unavailable agent) or a
strike forgiven by hand.

## Considered options

- **Option B, one snapshot of each track's facts with every stage's rule in one module.** Worth
  exploring, but it would have centralized the history-as-request mistake rather than removed it.
  That is why A went first.
- **A database.** The model was the problem, not the storage, and A fits in `queue.json`.

## Consequences

- **No migration.** `Canceled "re-cut requested"` and `"source swapped"` lines already on disk keep
  working through the history readers, which stay for failures (`ReopenedTracks`,
  `StageEligibility.IsOwed`). They go quiet as each track's next outcome lands. So the "read them as
  requests once, at load" step agreed on #50 was not needed. Nothing reads them as requests; they are
  honored the way they always were.
- `StemRecut` and `TrackSwap` no longer take a `JobStore`.
- A swap onto a track whose samples are already in `Mashup Samples` stays unsettled, as its
  reopened jobs did before. The library's files satisfy the plan, so nothing is cut and no handoff
  follows. Its row stays reachable until the user deals with it.
