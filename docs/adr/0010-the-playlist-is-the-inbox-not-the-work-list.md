# 0010 — the playlist is the inbox, not the work list

- Status: Accepted, and worth revisiting — see *Standing*
- Date: 2026-09-07

## Context

Re-cutting a track ([the feature](../../README.md), built 2026-09-06) deletes its staged files and
reopens separation, handoff and tagging so a different recipe runs. The first real use of it, on
`Groove Armada - At the River`, deleted the files and then **nothing happened**. The three stages
sat `Cancelled` for hours and no separation ever started.

The cause was not in the re-cut. `SeparationSelector` reads `QueueView.AwaitingSeparation`, which
is derived from the playlist's items — and At the River had left the playlist months earlier, the
moment it was fully processed. So re-cutting it created real work for a track **no work list could
express**. It could not be picked up, and it could not be drawn either: rows were composed from
the playlist plus the staging board, and a track mid-re-cut is briefly in neither.

Handoff and tagging never had this problem, because they read the disk. Separation was the only
stage still asking the playlist what to do.

## Decision

**Playlist membership is how work arrives, not what work is.**

A track's presence in the playlist is the signal that crateside should *start* on it. Once it has
been judged, what remains to do is a property of the track and of what is on disk — and it goes on
being true after the playlist entry is gone.

Concretely: video IDs with an open job in a stage **after judging** — separation, handoff or
tagging — join `QueueView.AwaitingSeparation`, whether or not the playlist still holds them.
`QueueItem.PlaylistItemId` becomes nullable, which it had been in fact since the day a row outlived
its playlist entry.

**They join that list and nothing else.** This is the whole safety argument and it is structural
rather than careful:

- `EvidenceSelector` and `VerdictSelector` read `QueueView.Outstanding`.
- `Outstanding` is built from `Items`.
- Reopened tracks are not in `Items`.

So a re-cut cannot buy a second judging — not because the re-cut code declines to ask for one, but
because there is no path from here to there. That matters more than it sounds: judging is the one
stage that spends the user's Claude quota ([ADR 0006](0006-a-verdict-session-costs-turns-times-context.md)),
and a guard that has to be remembered is not a guard.

Gated twice on the way in, for the same reason: only stages after judging count, and only tracks
the ledger has already judged.

## Consequences

**Rows gain a fourth source.** A track being re-cut has no playlist entry and no staged files, so
`TracksView` composes rows for reopened tracks too. Without it the row vanishes at exactly the
point there is most reason to watch it.

**Removal has to refuse.** A track with no playlist entry has nothing to remove; `QueueRemover`
says so rather than sending a null to the API, and the page hides the button.

**This does not extend to tracks whose audio has gone.** The working set is discarded once
separation has succeeded *and* staging is empty, so a track finished with long ago cannot be
re-cut — there is nothing to separate. Re-cut refuses those by asking `WorkingSet.TryGetSource`
rather than by inferring from staging. Making them re-cuttable needs a re-download, which is the
same machinery the user's *swap the source video* request needs, and is not built.

## Standing

**This decision was made mid-flow and under pressure**, in the same session that discovered the
fault, with the user's files already deleted and a track stuck. It was reported at the time and
not contradicted, and the user later agreed to it being recorded — but it did not get the
deliberation the other decisions here did, and it changes the shape of the pipeline rather than of
a view.

It is written down as accepted because it is what the code does. The part most worth re-examining
is whether *an open job after judging* is the right definition of work-to-be-done, or whether the
honest test is the one handoff and tagging already use: **look at the disk.** A separation work
list of "has a verdict, has no stems, has a working set" would need no job-store gate at all, and
would make the playlist purely an inbox rather than an inbox with an exception attached.
