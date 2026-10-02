# 0043 — a borrowed warp is finished, and says where it came from

- Status: Accepted; amended by [ADR 0044](0044-a-borrowed-warp-keeps-lives-transients-where-live-found-them.md), which takes Live's transients where Live found them and retires the reason lending ships off
- Date: 2026-10-02
- Amends [ADR 0037](0037-a-saved-default-clip-is-a-finished-warp.md). Builds
  [#54](https://github.com/Bamo16/crateside/issues/54).

## Context

ADR 0037 reads a saved default clip as a finished warp, because only the user's *Save Default Clip*
wrote one. That stopped being true when crateside learned to write sidecars through AbleKit. A stem
cut from the same upload as an instrumental the user has warped can be given that warp: its sidecar
written from the instrumental's clip and warp, with its own waveform and transients. Live applies
it, and needs the saved-clip byte set to do so. That was observed in the spikes of 2026-09-30 and
2026-10-01, recorded in #54 and in AbleKit's format write-up.

So a stem can now read as warped without the user having touched it, and its track as *ready to
move*. The user was asked whether that should count (2026-10-02).

## Decision

**A borrowed warp counts as finished.** It is the user's own warp, finished on a sibling, so the
board treats it as ADR 0037 treats any saved clip, and a track whose stems all carry one is ready to
move.

**crateside says where it came from.** It records every sidecar it writes, with its size and write
time, in `%APPDATA%\crateside\borrowed-warps.json`. A sample whose sidecar is still that file shows
*warped from Instrumental - Full* (or whichever sibling lent it) where it would show *warped*. Once
Live saves over the file, the sidecar no longer matches the record, and the warp is the user's.

**Who may lend a warp** (the user's rules in #54):

- **The same upload.** The sibling's provenance names the same video ID as the stem's, so the two
  were cut from one file and start together. Added sources never qualify.
- **An instrumental, karaoke, or the kept source.** A vocal is sometimes re-warped on its own
  afterwards, so its warp is not the track's. A kept source is recognised only by a name without
  a parenthetical.
- **Not adopted**, either way round, since an adopted file may be a render of a different length.
- **Its default clip saved.** Without that, Live ignores the warp.
- **The same length to the frame.** The sibling's head table records the frames Live analysed. On
  2026-10-02, 422 of 423 same-upload groups in the library matched to the frame. The exception
  differs by 44 s, so there is no tolerance.

**What crateside never does:** replace a sidecar whose default clip is saved, which is checked
again just before writing, or choose between siblings whose warps differ. Those are reported in
the log.

**Lending runs unattended, once it is switched on.** Once a minute, every staged stem without a
saved clip is tried against its siblings in staging and in Mashup Samples. That covers both
triggers in #54: a re-cut landing in staging, and the user saving one stem whose siblings are still
unwarped. Decoding uses the CPU and contends for nothing, like detection.

**It ships switched off** (`Warps:LendAutomatically` in appsettings). The user asked for it on once
the transient detector met its bar. On the library it does for instrumentals and drums but not for
vocals ([the note](../notes/2026-10-02-transients-against-lives.md)), and vocals are the stems most
often given a borrowed warp. A borrowed sidecar the user has not saved over is crateside's to
rewrite, so a better detector can later replace the transients it wrote.

## Consequences

- **A track can become ready to move while the user is away.** That is the point, and the stem
  row says which ones were borrowed, so a glance before the move shows which warps the user has
  not looked at.
- **A sibling saved early lends an early warp.** ADR 0037's caveat now applies to every stem cut
  from that upload, not just one.
- **The record can only lose information, never invent it.** A borrowed sidecar copied elsewhere,
  or the record file deleted, reads as the user's own warp. Nothing worse follows than a missing
  "from".
- **Transients are crateside's own.** Live never re-detects transients in a file it did not write.
  So the detector's quality is what the user sees in the clip view, and it is measured against
  Live's: [the note of 2026-10-02](../notes/2026-10-02-transients-against-lives.md).
