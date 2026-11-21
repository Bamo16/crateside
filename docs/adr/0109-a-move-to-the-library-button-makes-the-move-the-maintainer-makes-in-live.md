# 0109 - A "Move to the library" button makes the move the maintainer makes in Live

- Status: Accepted
- Date: 2026-11-19
- Amends [ADR 0003](0003-crateside-may-write-abletons-tag-store.md)'s "Never `Mashup Samples`":
  crateside may move files into the library when the maintainer presses this button. It still
  writes no tag there, and still never deletes from it.
- [ADR 0007](0007-the-move-out-of-sample-staging-is-the-warp-signal-per-sample.md) is unchanged.

## Context

A warp is finished when its stems leave Staging for the library (ADR 0007). Until now that move
was made only in Live, by dragging in the browser. The maintainer decided on 2026-11-19 that
crateside may do the same move for him from Track details.

## Decision

- **The button is a second hand on the same move, not a second signal.** crateside marks nothing
  and stores nothing. The track reads as finished because its stems are in the library, exactly
  as when Live moved them (ADR 0007), and every reader (the track's state, the guide keeper, the
  janitor) sees the same disk either way.
- **Which stems move: every sample of the track in Staging**, that is every staged file that is
  not a warp guide (`StagedSong.Samples`): the vocal and instrumental stems, and the source
  track of an instrumental release (ADR 0004). An adopted sample (ADR 0022) is one of them. The
  button moves the whole track or nothing, because it is offered only when the track is
  `ReadyToMove`: every staged sample carries a saved default clip (ADR 0037, 0107). A track with
  a sample still unwarped gets no button; that sample is still his turn.
- **Drums go to the drums folder.** The drums stem is the warp guide, and the library already
  files it in `Paths:KeptWarpGuides` (`Music\Stems\Drums`), outside Ableton's sight, once the
  track's samples are in the library (ADR 0046). The button does not file it separately: after
  a successful move it asks `WarpGuideKeeper` to keep the guide at once, rather than at the next
  minute's pass. The guide is never moved into `Mashup Samples`.
- **Where in the library a sample lands:** beside the track's files already there (the folder of
  its first library sample); with none, in the library root, which is where a drag from the
  Staging browser puts them.
- **A name already taken in the destination stops the move.** Nothing is overwritten and nothing
  is renamed in passing.
- **The `.asd` sidecar moves with its audio**, always as a pair. A sample with no sidecar moves
  alone.
- **A file Live holds open: all or nothing.** Before anything moves, crateside opens every audio
  file and sidecar to be moved exclusively; if any open fails, nothing is moved and the answer
  names the file that is held and says to close the set in Live. If a move still fails partway
  (a file taken between the check and the move), every file already moved goes back to Staging
  and the same answer is given. A rollback that itself fails is named in the answer, with each
  file's place.
- **No tag is written** and none is removed. The Staging tag store keeps its entries for the
  departed files, as it does after Live's move (ADR 0007's corroboration).
- **Sets are not relinked** (decided 2026-11-20). The button moves files and touches no set.
  - Live finds a moved file by its name, in its Places and user-library folders, and repairs the
    link itself, often without asking. The move leaves the stems' names alone, so Live can find
    them.
  - A rename changes the name, so Live cannot find the file; that is why `TrackRename` relinks and
    this button does not.
  - Other people may not want crateside touching their sets, may keep sets in many places (some
    offline), or may not have told crateside where their sets are.
  - The tip that makes it work, for the manual workflow: add `Mashup Samples` and the drums folder
    to Live's Places.
- **Where the button sits:** on Track details, in the Next group, labelled *Move to the library*.
  It shows only while the track is `ReadyToMove`. Pressing it asks nothing first: the move is
  undone by moving the files back, and nothing is deleted. The press is recorded in the actions
  log (`MovedToLibrary`).

## Consequences

- ADR 0003's boundary is now: tags in the library only for a rename (ADR 0039); files into the
  library only by this button or a re-cut's direct handoff (ADR 0103); never a deletion without
  the maintainer's confirmation (ADR 0103).
- The existing signal, the existing state and the existing guide keeping are untouched, so Live's
  move and the button's move cannot diverge.
