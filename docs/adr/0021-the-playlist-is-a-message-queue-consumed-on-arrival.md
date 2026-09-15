# 0021 — the playlist is a message queue, consumed on arrival

- Status: **Proposed.** Nothing is built, and the premise is recorded ahead of the design
- Date: 2026-09-15
- Answers: the *Standing* question in
  [ADR 0010](0010-the-playlist-is-the-inbox-not-the-work-list.md)

## Context

ADR 0010 established that "playlist membership is how work arrives, not what work is", and closed
by naming its own weakest part: whether *an open job after judging* is the right definition of
work-to-be-done, "or whether the honest test is the one handoff and tagging already use: **look at
the disk**."

That question went unanswered for eight days because the premise underneath it had never been
stated. On 2026-09-15 the user stated it, unprompted:

> I never care about what's in this playlist. I only use it as a black hole of sorts I can add any
> track to and know that crateside will pick it up. I would probably prefer that crateside spent
> less time thinking about what is or is not in the playlist and treat it like a message queue.
> Once crateside becomes aware of a new item in the playlist, I think it should store whatever info
> it needs about the item immediately so that it can just remove it from the playlist immediately
> and be done with it.

This is **owned, not measured and not inferred** — it is a fact about how the user works, of
exactly the kind ADR 0002's postscript says to check rather than reason about.

## The fault it explains, which recurred

Measured on 2026-09-15. A source added to *Eminem - Medicine Ball* — a finished track the playlist
no longer held — was never fetched. No job for it ever existed, while the download stage ran
repeatedly for other videos.

`DownloadSelector.Added` reads `QueueView.Everything`, which is `Items ∪ AwaitingSeparation ∪
AwaitingKey`; `Items` is the playlist plus pasted tracks. `ReopenedTracks` — the mechanism ADR 0010
added for tracks the playlist no longer holds — enumerates **Separation, Handoff and Tagging by
name**, and a download is none of them. So the track was in no list at all.

Running the real selector offline against composed views confirms the shape:

| the finished track is… | the selector picks |
|---|---|
| in the playlist, nothing else wants audio | the added source |
| in the playlist, a fresh track wants audio | the fresh track |
| **absent from the playlist** | **the added source: never** |

**This is the same fault as ADR 0010's, one stage over.** There, a re-cut created work for a track
no work list could express, and it was fixed by naming three stages. Here a download did, and the
naming did not cover it. A mechanism that must be extended per stage reintroduces the hole every
time a stage is added — which is the argument for ADR 0010's own alternative, not its decision.

## Decision (proposed)

**Playlist membership stops being readable state.** An entry is a message: crateside takes what it
needs from it on arrival, removes it, and never asks again whether it is there.

Two halves, and the first is worth having even alone:

1. **Nothing derives work from membership.** Work-to-be-done is read from the disk, the ledger and
   the plan — all of which go on being true after the entry is gone. This is ADR 0010's *Standing*
   alternative, adopted.
2. **Consumption removes the entry**, rather than removal being a manual button for the
   fully-processed (`RemoveProcessedAsync`, today in Settings).

## Why this is Proposed rather than Accepted

- **Nothing is built.** Recording it now so the premise stops being unwritten, which is the whole
  reason it was said.
- **"Whatever info it needs" is undecided**, and it is the real design question. An entry currently
  carries title, channel, added-at, position and premium outlook, and rows are composed partly from
  it. Consuming on arrival means persisting that somewhere, and `queue.json` holds instructions
  rather than observations.
- **Doing half of it is worse than doing neither.** Removing entries on arrival while anything
  still derives work from membership would orphan tracks faster, not slower. Order matters: (1)
  before (2).
- The immediate orphan — Medicine Ball's added source — can be unblocked without any of this, and
  that fix should not be mistaken for this decision.

## Consequences if adopted

- `QueueRemover`'s refusal for entries that do not exist stays load-bearing and gets exercised far
  more often.
- ADR 0010's structural safety argument — that judging cannot be bought twice because reopened
  tracks are not in `Items` — **rests on `Items` meaning "the playlist"** and must be re-derived
  some other way before `Items` stops meaning that. This is the sharpest constraint here: judging
  spends the user's Claude quota ([ADR 0006](0006-a-verdict-session-costs-turns-times-context.md)),
  and the guard protecting it is the thing this decision dissolves.
