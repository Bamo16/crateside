# 0021 — the playlist is a message queue, consumed on arrival

- Status: **Accepted.** Built 2026-09-15; see *As built* for what was and was not done
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

## Decision

**Playlist membership stops being readable state.** An entry is a message: crateside takes what it
needs from it on arrival, removes it, and never asks again whether it is there.

Two halves, and the first is worth having even alone:

1. **Nothing derives work from membership.** Work-to-be-done is read from the disk, the ledger and
   the plan — all of which go on being true after the entry is gone. This is ADR 0010's *Standing*
   alternative, adopted.
2. **Consumption removes the entry**, rather than removal being a manual button for the
   fully-processed (`RemoveProcessedAsync`, today in Settings).

## As built

`IntakeStore` (`%APPDATA%\crateside\intake.json`) records a track the first time it is seen —
video ID, title, added-at, premium outlook, availability — and `IntakeStore.Merge` gives the scan
the playlist's entries followed by everything it has ever held. `Backfill` takes in tracks known
only from a stored plan, which is how a track that left the playlist *before* any of this existed
becomes reachable at all.

Two things the proposal got wrong, both in the safe direction:

- **`QueueView.Compose` did not have to change.** The merge happens before it, so the signature
  stands and none of the 14 call sites in tests moved. The estimate of widespread churn was wrong.
- **The quota guard was never structural in the first place.** `EvidenceSelector` requires the
  audio to be in the working set — which the janitor discards once separation has succeeded and
  staging is empty — and `VerdictSelector` requires the ledger to have no run for the track.
  Either refuses a finished track on its own. `QuotaGuardTests` pins both, and was written and
  run *before* the change so that it pinned existing behaviour rather than new.

**The download stage now takes a kept or attached source before audio.** Audio had been first
since ADR 0019, and the audio list refills from the playlist while the other two are bounded by
something the user did — so audio going first starved them for as long as anything was arriving.
That is the half of the Medicine Ball fault that had nothing to do with membership, and it had no
tests at all: `DownloadSelectorTests` is new.

**Consumption is behind a setting, off by default.** Turning it on offers back every entry crateside
already knows of, once each, and a playlist deletion cannot be undone from here. See the backoff
amendment below: as first built it re-offered them on every scan and spent the day's quota.

## Deliberately not done

- **`ReopenedTracks.From` is still there.** The proposal said delete it. It is now redundant for
  any track with an intake record, but it is the only thing covering a track being re-cut that has
  neither a record nor a stored plan, and removing it would trade a fixed bug for a possible one.
  It should go once the intake has been running long enough to have seen everything.
- **`QueueItem.ChannelTitle` and `Position` are still there**, though nothing reads either. Dead,
  and not worth widening this change for.

## Why this was recorded before it was built

The premise was the thing that had been missing for eight days, so it went down first and the
design followed. Two of the reasons for waiting were settled by building it: *what to persist*
turned out to be five fields, of which only `Title`, `PremiumOutlook`, `IsUnavailable` and
`AddedAt` are ever read; and the guard the decision seemed to dissolve turned out not to rest on
it.

The third stands and shaped the result: **doing half of this is worse than doing neither.**
Removing entries while anything still derived work from membership would have orphaned tracks
faster, not slower — so nothing is consumed until the intake is what the work lists are built
from, and consumption is off until asked for.

## Consequences

- `QueueRemover`'s refusal for entries that do not exist stays load-bearing and gets exercised far
  more often.
- **ADR 0010's structural safety argument is gone, and was never what held.** It said judging
  could not be bought twice because reopened tracks were kept out of `Items`; `Items` no longer
  means "the playlist", so that is spent. What actually refuses a second judging is a working set
  the janitor has discarded and a ledger that already has a run — both reads of state that
  outlives the entry, which is the same argument as the rest of this decision. Judging spends the
  user's Claude quota ([ADR 0006](0006-a-verdict-session-costs-turns-times-context.md)), so the
  guard is now pinned by tests rather than by a shape.
- **`Items` grows without bound.** Every track ever taken on stays in it, where before the
  playlist forgot them. At this library's size that is a few hundred records and costs nothing;
  if it ever matters, the answer is to drop records the disk and the ledger agree are finished,
  not to go back to asking the playlist.


## Amendment — 2026-09-15: an unreadable playlist is no longer a failed scan

Listed above as deliberately not done, and it took hours to bite. Consuming entries spent the
day's YouTube quota (see the *runaway* note below); the playlist then refused every read, the scan
threw, and the Tracks page sat on "Reading the queue…" with nothing on it — while the library, the
ledger and the intake all sat on disk needing no network at all.

**A playlist that cannot be read now means no new arrivals.** `ItemsAsync` reports the refusal as
`PlaylistProblem` and gives back what it last held, the scan completes, and the page draws its rows
with the refusal beside them rather than instead of them. Nothing is consumed while a read is
failing: what is held is then stale or empty, and an entry is offered back only once.

This is the decision the *Standing* section of ADR 0010 pointed at, arrived at from the other end.
The reason it was deferred — that it changes how four drainers behave when YouTube is unreachable —
turned out to be the point rather than the risk: they now go on working the tracks they already
have.

## Amendment — 2026-09-15: consumption had no backoff, and spent a day's quota

`ConsumeAsync` re-offered every recorded entry the playlist still held on **every** scan, and a
scan runs on each page read and each stage worker's two-minute loop. A `playlistItems.delete`
costs fifty quota units against a daily ten thousand. The removals were evidently failing — a 403
from a playlist the authorised channel does not own would do it — and the result checked nothing,
so they were retried until the quota was gone.

Every other stage in this repo goes through `StageEligibility` and strikes out. This one was
written without any of that.

**An entry is now marked as offered before the call and regardless of the result**, so it is
offered once and never again; a removal that fails stays failed, and retrying is the user's to ask
for. The cost is stated in Settings, where it was not before: emptying a hundred entries spends
half a day's quota, which is inherent to the feature rather than a fault, and nobody had said so.
