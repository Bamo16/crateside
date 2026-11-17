# 0046 — a filed track's warp guide is kept outside Ableton

- Status: Accepted; refined by [ADR 0103](0103-a-library-file-can-be-deleted-and-a-recut-keeps-the-saved-warp.md) (2026-11-17)
- Date: 2026-10-02
- Builds [#87](https://github.com/Bamo16/crateside/issues/87).

## Context

The warp guide (the drums stem) is what the user warps a group on: muted, with markers placed on
its transients while listening to the instrumental and vocal. Until now crateside told the user to
delete it before moving the track to Mashup Samples.

Drum stems with their sidecars turned out to be the best input for testing beatdetect with FiLL:
on a drum stem nearly every transient Live detects is a real hit. There were only a few to use,
because drum stems lived only in staging and most of those were not yet warped. So the user
stopped deleting them and left them in staging, which also leaves them in Ableton's browser. They
want them kept, but not in Ableton and not in view.

Live browses four places on this machine: Mashup Samples, Sample Staging, and the two ASD test
folders under `Music\Stems` (read from its `Library.cfg`, 2026-10-02).

## Decision

**Once every other cut of a track has left staging and at least one is in Mashup Samples,
crateside moves its warp guide out of staging**, to `Music\Stems\Drums` (`Paths:KeptWarpGuides`),
together with its `.asd` if it has one. The move runs unattended once a minute, since it contends
for nothing. A guide whose track still has samples in staging stays, because the user may still be
warping.

- **Unwarped guides are kept too.** They cost 30–50 MB each, and the instrumental's warp still
  describes them, since every cut of an upload has the same length (ADR 0044).
- **A kept guide is a snapshot and is never overwritten.** If the name is taken, the newcomer gets
  today's date (`Song (Drums) (2026-10-02).flac`).
- **The guide and its sidecar move together or not at all.** A locked file, usually one open in
  Ableton, is reported and tried again a minute later.
- **crateside never reads that folder as samples.** It is in neither staging nor the library.
- **Removing a track leaves its kept guide**, as it leaves the track's files in Mashup Samples:
  removal never reaches past staging (ADR 0027), and a guide is kept only once its track is filed.

The track's next step no longer mentions the guide: *warped — move to Mashup Samples*.

## Consequences

- **Every track warped from now on adds a drum stem with Live's transients to a corpus** for
  beatdetect and FiLL's automix work (#68, #83).
- **On 2026-10-02 the first pass moves 10 guides** left in staging after their tracks were filed.
  The other 26 belong to tracks still in staging.
- **A set that used a guide from staging loses it**, just as it did when the guide was deleted.

## Since ADR 0103

Keeping stands: a guide still moves to `Paths:KeptWarpGuides` once its track is filed, and the kept
folder is an archive only. What changed is what the guide is for afterwards. Nothing brings a kept
guide back to Staging when its track returns there. A swapped source replaces every stem, so its
recipe asks for a fresh guide cut from the new audio (`TrackSwapTests`), and a re-cut that finds a
guide already on disk asks for none. An added source gets no guide of its own yet: separation is
per playlist track, and ADR 0103's "Not built" says why.
