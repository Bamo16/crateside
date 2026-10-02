# 0047 — a starter warp, and a record of where every written transient came from

- Status: Accepted
- Date: 2026-10-02
- Builds [#77](https://github.com/Bamo16/crateside/issues/77). Amends
  [ADR 0043](0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md) and
  [ADR 0044](0044-a-borrowed-warp-keeps-lives-transients-where-live-found-them.md).

## Context

#77 asked for a one-marker warp at keytag's BPM on every stem of a first cut. When the group is
dragged into an empty arrangement, the clip's tempo then sets the arrangement's, which saves a step
at the start of every warp. #54 settled how crateside writes sidecars: Live needs the saved-clip
byte set, and a record of the sidecars crateside wrote tells them apart from the user's.

Two things the user said on 2026-10-02 shape this:

- **A warp is the user's once they save it.** Dragging the clips in, finding the verse, adjusting
  the offset or the downbeat and pressing *Save Default Clip* is a sign-off. The result is as
  trustworthy as a warp placed by hand.
- **Raw data for FiLL may want Live's transients rather than crateside's.** The user's sign-off
  does not touch them: Live never detects transients in a sidecar it did not write
  (docs/notes/2026-10-02-transients-against-lives.md). So once the user saves, the warp is theirs,
  but the transients are still whatever crateside wrote. The record of written sidecars stops
  applying at that moment and cannot say where the transients came from. The user also asked
  that this be versioned, since the detector may improve over time, as beatdetect and keydetect do.

## Decision

**A starter warp.** On each staged stem of an upload nothing is warped on yet, crateside writes one
marker at the start of the clip at keytag's `bpm.value`, laid out as Live saves a one-marker warp:
beat 0 at the start of the audio, the clip running to the end of the file, looping off, Complex
mode. Every stem of the group gets the same marker. An upload not yet judged gets none until it is.

- **It is not a finished warp.** Tracks reads its stem as unwarped, it gives no tempo for ground
  truth or Counts, and it never lends.
- **A lent warp replaces it**, as soon as the user saves a warp on any cut of the upload.
- **Once Live saves over it, it is the user's warp**, by the same record ADR 0043 keeps. That record
  is now `written-warps.json` (it was `borrowed-warps.json`, which never held an entry) and also
  marks starters. ADR 0043's rule that a borrowed sidecar the user has not saved over is crateside's
  to rewrite is now what the code does: a lent warp also replaces an earlier borrowed one.
- **It is switched off** (`Warps:WriteStarterWarps`), like lending, until the user has checked a
  borrowed warp in Live. A starter is only written when nothing could be lent, so with lending off
  a stem whose upload is warped elsewhere gets neither.

**Every transient list crateside writes is recorded by fingerprint**, in `transients.json`: a hash
of the positions, with the detector and its version (`onsets 1`) or the Live file it was copied
from. The positions are what Live keeps when it saves over the warp. The record is keyed by content
rather than path, so it survives the user's sign-off, a move to Mashup Samples and a rename.

- **Whatever looks for Live's transients consults it.** A list crateside detected is never taken
  for Live's, wherever it turns up. That replaces ADR 0044's check on the borrowed-warp record, which
  forgot a sidecar as soon as the user saved it.
- **A copy of Live's transients counts only on the file it was written to.** It is the same list as
  the one it was copied from, which is genuinely Live's.
- **The detector's version is raised whenever a change moves what it finds**, so data from an
  earlier version can be told apart and redone.

## Consequences

- **A data pull for FiLL can tell Live's transients from crateside's**, as far back as this record
  goes. Sidecars written before it (only the ASD spike's) are not in it.
- **The marker sits at the clip start, not at the drums.** Placing it on the user's first clear
  snare is research of its own (#77, 2026-10-02).
- **A wrong BPM costs no more than today's default of 120.** keytag's recommendation matched the
  warped tempo exactly on 223 of 351 songs (ADR 0041, 2026-09-27).
- **That transients survive *Save Default Clip* unchanged is inferred, not measured.** It joins the
  user's check of the spike in Live.
